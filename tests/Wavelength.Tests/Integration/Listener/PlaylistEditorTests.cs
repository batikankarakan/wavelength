using System.Net;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Integration.Listener;

/// <summary>Listener page 3: create / edit playlist, cover, add-track and delete.</summary>
[Collection("T2 uploads")]
public class PlaylistEditorTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public PlaylistEditorTests(WavelengthFactory app) => _app = app;

    private Task<int> TrackId(string title) => _app.Db(db => db.Tracks.Where(t => t.Title == title).Select(t => t.Id).FirstAsync());
    private Task<int> PlaylistId(string title) => _app.Db(db => db.Playlists.Where(p => p.Title == title).Select(p => p.Id).FirstAsync());
    private Task<List<int>> Order(int playlistId) =>
        _app.Db(db => db.PlaylistTracks.Where(x => x.PlaylistId == playlistId).OrderBy(x => x.Position).Select(x => x.TrackId).ToListAsync());

    private static int IdFrom(string? location) => int.Parse(location!.Split('/').Last());

    [Fact]
    public async Task New_playlist_form_can_be_prefilled_with_a_track()
    {
        var id = await TrackId("Sugar Signal");
        var s = await _app.LoginAs("deniz@example.com");
        var page = await s.Get($"/playlists/new?trackId={id}");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Sugar Signal", page.Html);
    }

    [Fact]
    public async Task Title_is_required()
    {
        var s = await _app.LoginAs("deniz@example.com");
        var before = await _app.Db(db => db.Playlists.CountAsync());
        var res = await s.Post("/playlists/new", new Dictionary<string, string> { ["Form.Title"] = "  ", ["Form.IsPublic"] = "true" });
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains("Give your playlist a title.", res.Text);
        Assert.Equal(before, await _app.Db(db => db.Playlists.CountAsync()));
    }

    [Fact]
    public async Task Create_keeps_order_and_drops_unpublished_tracks()
    {
        var a = await TrackId("Neon Highway");
        var b = await TrackId("Static Bloom");
        var pending = await TrackId("After Hours Club");
        var s = await _app.LoginAs("deniz@example.com");

        var res = await s.Post("/playlists/new", new List<KeyValuePair<string, string>>
        {
            new("Form.Title", "  Road Trip  "), new("Form.Description", "for the car"), new("Form.IsPublic", "false"),
            new("Form.TrackIds", b.ToString()), new("Form.TrackIds", pending.ToString()), new("Form.TrackIds", a.ToString()), new("Form.TrackIds", b.ToString()),
        });
        Assert.Equal(HttpStatusCode.Redirect, res.Status);
        var id = IdFrom(res.Location);
        var p = await _app.Db(db => db.Playlists.FirstAsync(x => x.Id == id));
        Assert.Equal("Road Trip", p.Title);
        Assert.Equal("for the car", p.Description);
        Assert.False(p.IsPublic);
        Assert.Equal(new[] { b, a }, await Order(id));
        Assert.Equal(new[] { 1, 2 }, await _app.Db(db => db.PlaylistTracks.Where(x => x.PlaylistId == id).OrderBy(x => x.Position).Select(x => x.Position).ToListAsync()));
    }

    [Fact]
    public async Task Edit_rewrites_positions_and_settings()
    {
        var id = await PlaylistId("Chrome & Neon"); // owner: maya
        var original = await Order(id);
        var reversed = original.AsEnumerable().Reverse().Take(3).ToList();
        var s = await _app.LoginAs("maya@example.com");

        Assert.Equal(HttpStatusCode.OK, (await s.Get($"/playlists/{id}/edit")).Status);
        var fields = new List<KeyValuePair<string, string>> { new("Form.Title", "Chrome Only"), new("Form.Description", ""), new("Form.IsPublic", "false") };
        fields.AddRange(reversed.Select(t => new KeyValuePair<string, string>("Form.TrackIds", t.ToString())));
        var res = await s.Post($"/playlists/{id}/edit", fields);
        Assert.Equal($"/playlists/{id}", res.Location);

        Assert.Equal(reversed, await Order(id));
        var p = await _app.Db(db => db.Playlists.FirstAsync(x => x.Id == id));
        Assert.Equal("Chrome Only", p.Title);
        Assert.Null(p.Description);
        Assert.False(p.IsPublic);
    }

    [Fact]
    public async Task Cover_upload_and_removal()
    {
        var s = await _app.LoginAs("leo@example.com");
        var res = await s.PostMultipart("/playlists/new",
            T2Files.Form(("Form.Title", "With Cover"), ("Form.IsPublic", "true")).File("cover", "c.png", "image/png", T2Files.Png));
        Assert.Equal(HttpStatusCode.Redirect, res.Status);
        var id = IdFrom(res.Location);
        var url = await _app.Db(db => db.Playlists.Where(p => p.Id == id).Select(p => p.CoverUrl).FirstAsync());
        Assert.StartsWith("/uploads/covers/", url);
        Assert.True(await _app.FileExists(url!));

        await s.Post($"/playlists/{id}/edit", new Dictionary<string, string> { ["Form.Title"] = "With Cover", ["Form.IsPublic"] = "true", ["Form.RemoveCover"] = "true" });
        Assert.Null(await _app.Db(db => db.Playlists.Where(p => p.Id == id).Select(p => p.CoverUrl).FirstAsync()));
        Assert.False(await _app.FileExists(url!));
    }

    [Fact]
    public async Task Bad_cover_is_rejected()
    {
        var s = await _app.LoginAs("leo@example.com");
        var res = await s.PostMultipart("/playlists/new",
            T2Files.Form(("Form.Title", "Bad Cover")).File("cover", "c.txt", "text/plain", new byte[] { 1, 2, 3 }));
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains("Images must be JPG, PNG, WEBP or GIF.", res.Text);
        Assert.False(await _app.Db(db => db.Playlists.AnyAsync(p => p.Title == "Bad Cover")));
    }

    [Fact]
    public async Task Someone_elses_playlist_cannot_be_edited_or_deleted()
    {
        var id = await PlaylistId("Late Night Coding"); // owner: listener
        var s = await _app.LoginAs("deniz@example.com");

        AssertForbidden(await s.Get($"/playlists/{id}/edit"));
        AssertForbidden(await s.Post($"/playlists/{id}/edit", new Dictionary<string, string> { ["Form.Title"] = "Hacked" }));
        AssertForbidden(await s.Post($"/playlists/{id}/delete", new { }));
        Assert.Equal("Late Night Coding", await _app.Db(db => db.Playlists.Where(p => p.Id == id).Select(p => p.Title).FirstAsync()));

        Assert.Equal(HttpStatusCode.NotFound, (await s.Get("/playlists/999999/edit")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post("/playlists/999999/edit", new Dictionary<string, string> { ["Form.Title"] = "x" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post("/playlists/999999/delete", new { })).Status);
    }

    [Fact]
    public async Task Owner_deletes_playlist_and_its_saves()
    {
        var id = await PlaylistId("Group Project Panic"); // owner: emre, saved by deniz
        var s = await _app.LoginAs("emre@wavelength.app");
        var res = await s.Post($"/playlists/{id}/delete", new { });
        Assert.Equal("/library/playlists", res.Location);
        Assert.False(await _app.Db(db => db.Playlists.AnyAsync(p => p.Id == id)));
        Assert.False(await _app.Db(db => db.SavedPlaylists.AnyAsync(p => p.PlaylistId == id)));
        Assert.False(await _app.Db(db => db.PlaylistTracks.AnyAsync(p => p.PlaylistId == id)));
    }

    [Fact]
    public async Task Add_track_appends_once_for_the_owner_only()
    {
        var id = await PlaylistId("Rainy Window"); // owner: huseyin
        var extra = await TrackId("Iron Lullaby");
        var pending = await TrackId("Moth to the Flame");
        var owner = await _app.LoginAs("huseyin@wavelength.app");
        var before = await Order(id);

        var res = await owner.Post($"/playlists/{id}/add-track", new { trackId = extra, returnUrl = "/library" });
        Assert.Equal("/library", res.Location);
        var after = await Order(id);
        Assert.Equal(before.Append(extra), after);
        Assert.Equal(before.Count + 1, await _app.Db(db => db.PlaylistTracks.Where(x => x.PlaylistId == id && x.TrackId == extra).Select(x => x.Position).FirstAsync()));

        // duplicate: nothing changes, default return url is the track page
        var dup = await owner.Post($"/playlists/{id}/add-track", new { trackId = extra, returnUrl = "https://evil.example.com" });
        Assert.Equal($"/tracks/{extra}", dup.Location);
        Assert.Equal(after, await Order(id));
        Assert.Contains("Already in", (await owner.Get("/library")).Text);

        // unpublished tracks cannot be added
        await owner.Post($"/playlists/{id}/add-track", new { trackId = pending });
        Assert.DoesNotContain(pending, await Order(id));
        Assert.Contains("not available", (await owner.Get("/library")).Text);

        var stranger = await _app.LoginAs("leo@example.com");
        AssertForbidden(await stranger.Post($"/playlists/{id}/add-track", new { trackId = await TrackId("Coffee Cloud") }));
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Post("/playlists/999999/add-track", new { trackId = extra })).Status);
    }

    /// <summary>MVC 5 answers with the 403 page directly; the Core version redirected to /403. Both mean "forbidden".</summary>
    private static void AssertForbidden(Session.Page page) =>
        Assert.True(page.Status == HttpStatusCode.Forbidden || (page.Location ?? "").Contains("/403"), $"expected 403, got {page.Status} {page.Location}");
}
