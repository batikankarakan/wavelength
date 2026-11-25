using System.Net;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Tests.Integration.Listener;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Integration.Studio;

/// <summary>Studio pages 1, 5 and 6: dashboard numbers, albums and artist profile settings.</summary>
[Collection("T2 uploads")]
public class AlbumsProfileDashboardTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public AlbumsProfileDashboardTests(WavelengthFactory app) => _app = app;

    private Task<Album?> AlbumBy(string title) => _app.Db(db => db.Albums.AsNoTracking().FirstOrDefaultAsync(a => a.Title == title));
    private Task<int> TrackId(string title) => _app.Db(db => db.Tracks.Where(t => t.Title == title).Select(t => t.Id).FirstAsync());

    // ---------- dashboard ----------

    [Fact]
    public async Task Dashboard_numbers_match_the_database()
    {
        var expected = await _app.Db(async db =>
        {
            var a = await db.ArtistProfiles.FirstAsync(x => x.StageName == "Mio Tanaka");
            var since = DateTime.UtcNow.Date.AddDays(-29);
            return new[]
            {
                await db.Tracks.Where(t => t.ArtistId == a.Id).SumAsync(t => t.PlaysCount),
                await db.ListeningHistory.CountAsync(h => h.Track.ArtistId == a.Id && h.PlayedAt >= since),
                await db.TrackLikes.CountAsync(l => l.Track.ArtistId == a.Id),
                await db.Follows.CountAsync(f => f.FollowedId == a.UserId),
                await db.ShareLinks.Where(s => s.Track.ArtistId == a.Id).SumAsync(s => s.ClickCount),
            };
        });
        var s = await _app.LoginAs("mio@wavelength.app");
        var page = await s.Get("/studio");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var kpis = page.Doc.QuerySelectorAll("[data-count]").Take(5).Select(e => int.Parse(e.GetAttribute("data-count")!)).ToArray();
        Assert.Equal(expected, kpis);
        Assert.True(expected[0] > 0 && expected[2] > 0 && expected[4] == 21); // seeded "rainlofi" share link has 21 clicks
        Assert.Contains("Slow Burn Rain", page.Text); // top tracks
    }

    // ---------- albums ----------

    [Fact]
    public async Task Albums_page_lists_own_albums_and_singles()
    {
        var s = await _app.LoginAs("kora@wavelength.app");
        var page = await s.Get("/studio/albums");
        Assert.Contains("Night Transit", page.Text);
        Assert.Contains("After Hours Club", page.Text); // single (no album)
        Assert.DoesNotContain("Chrome Hearts", page.Text);
    }

    [Fact]
    public async Task Create_album_with_cover()
    {
        var s = await _app.LoginAs("djatlas@wavelength.app");
        var res = await s.PostMultipart("/studio/albums",
            T2Files.Form(("NewAlbum.Title", "  Skyline Tapes "), ("NewAlbum.Type", "ep"), ("NewAlbum.ReleaseDate", "2026-11-01"))
                .File("NewAlbum.Cover", "c.png", "image/png", T2Files.Png));
        Assert.Equal(HttpStatusCode.Redirect, res.Status);
        var a = await AlbumBy("Skyline Tapes");
        Assert.NotNull(a);
        Assert.Equal("ep", a!.Type);
        Assert.Equal(new DateOnly(2026, 11, 1), a.ReleaseDate);
        Assert.StartsWith("/uploads/covers/", a.CoverUrl);
        Assert.Equal($"/studio/albums#album-{a.Id}", res.Location);
    }

    [Theory]
    [InlineData("", "album", "2026-01-01", "Give the album a title.")]
    [InlineData("Weird", "mixtape", "2026-01-01", "Choose single, EP or album.")]
    [InlineData("Old", "album", "1850-01-01", "Enter a realistic release date.")]
    [InlineData("Future", "album", "2099-01-01", "Enter a realistic release date.")]
    public async Task Create_album_validation(string title, string type, string date, string error)
    {
        var before = await _app.Db(db => db.Albums.CountAsync());
        var s = await _app.LoginAs("lumen@wavelength.app");
        var res = await s.Post("/studio/albums", new Dictionary<string, string> { ["NewAlbum.Title"] = title, ["NewAlbum.Type"] = type, ["NewAlbum.ReleaseDate"] = date });
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains(error, res.Text);
        Assert.Equal(before, await _app.Db(db => db.Albums.CountAsync()));
    }

    [Fact]
    public async Task Update_album_and_refuse_bad_input()
    {
        var album = await AlbumBy("Sugar Signal");
        var s = await _app.LoginAs("lumen@wavelength.app");

        var ok = await s.PostMultipart($"/studio/albums/{album!.Id}",
            T2Files.Form(("Title", "Sugar Signal (Deluxe)"), ("Type", "album"), ("ReleaseDate", "2026-10-01"))
                .File("Cover", "c.png", "image/png", T2Files.Png));
        Assert.Equal($"/studio/albums#album-{album.Id}", ok.Location);
        var updated = await _app.Db(db => db.Albums.AsNoTracking().FirstAsync(a => a.Id == album.Id));
        Assert.Equal("Sugar Signal (Deluxe)", updated.Title);
        Assert.Equal("album", updated.Type);
        Assert.StartsWith("/uploads/covers/", updated.CoverUrl);

        await s.Post($"/studio/albums/{album.Id}", new Dictionary<string, string> { ["Title"] = "X", ["Type"] = "mixtape", ["ReleaseDate"] = "2026-10-01" });
        Assert.Equal("Sugar Signal (Deluxe)", (await _app.Db(db => db.Albums.AsNoTracking().FirstAsync(a => a.Id == album.Id))).Title);
        Assert.Contains("Choose single, EP or album.", (await s.Get("/studio/albums")).Text);
    }

    [Fact]
    public async Task Delete_album_turns_its_tracks_into_singles()
    {
        var album = await AlbumBy("Rainy Window Tapes");
        var trackIds = await _app.Db(db => db.Tracks.Where(t => t.AlbumId == album!.Id).Select(t => t.Id).ToListAsync());
        Assert.NotEmpty(trackIds);
        var s = await _app.LoginAs("mio@wavelength.app");

        var res = await s.Post($"/studio/albums/{album!.Id}/delete", new { });
        Assert.Equal("/studio/albums", res.Location);
        Assert.Null(await AlbumBy("Rainy Window Tapes"));
        Assert.False(await _app.Db(db => db.SavedAlbums.AnyAsync(x => x.AlbumId == album.Id)));
        Assert.All(await _app.Db(db => db.Tracks.Where(t => trackIds.Contains(t.Id)).Select(t => t.AlbumId).ToListAsync()), a => Assert.Null(a));
    }

    [Fact]
    public async Task Assign_and_unassign_tracks()
    {
        var album = await AlbumBy("Night Transit");
        var single = await TrackId("After Hours Club");
        var s = await _app.LoginAs("kora@wavelength.app");

        Assert.Equal($"/studio/albums#album-{album!.Id}", (await s.Post($"/studio/albums/{album.Id}/tracks", new { trackId = single })).Location);
        Assert.Equal(album.Id, await _app.Db(db => db.Tracks.Where(t => t.Id == single).Select(t => t.AlbumId).FirstAsync()));

        await s.Post($"/studio/albums/{album.Id}/tracks/{single}/remove", new { });
        Assert.Null(await _app.Db(db => db.Tracks.Where(t => t.Id == single).Select(t => t.AlbumId).FirstAsync()));

        // removing a track that isn't on that album
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/studio/albums/{album.Id}/tracks/{single}/remove", new { })).Status);
    }

    [Fact]
    public async Task Other_artists_albums_and_tracks_are_refused()
    {
        var foreignAlbum = await AlbumBy("Chrome Hearts");
        var myAlbum = await AlbumBy("Blue Hour");
        var foreignTrack = await TrackId("Gravity Wells");
        var s = await _app.LoginAs("selin@wavelength.app");

        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/studio/albums/{foreignAlbum!.Id}", new Dictionary<string, string> { ["Title"] = "Mine", ["Type"] = "album", ["ReleaseDate"] = "2026-01-01" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/studio/albums/{foreignAlbum.Id}/delete", new { })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/studio/albums/{myAlbum!.Id}/tracks", new { trackId = foreignTrack })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/studio/albums/{foreignAlbum.Id}/tracks", new { trackId = await TrackId("Tidal Memory") })).Status);
        Assert.NotNull(await AlbumBy("Chrome Hearts"));
    }

    // ---------- artist profile ----------

    [Fact]
    public async Task Profile_update_banner_and_removal()
    {
        var s = await _app.LoginAs("blackember@wavelength.app");
        Assert.Contains("Black Ember", (await s.Get("/studio/profile")).Html);

        var res = await s.PostMultipart("/studio/profile",
            T2Files.Form(("Form.StageName", "  Black Ember Live "), ("Form.Country", " "), ("Form.Website", "https://ember.example.com"))
                .File("Form.Banner", "b.png", "image/png", T2Files.Png));
        Assert.Equal("/studio/profile", res.Location);
        var a = await _app.Db(db => db.ArtistProfiles.AsNoTracking().FirstAsync(x => x.User.Username == "blackember"));
        Assert.Equal("Black Ember Live", a.StageName);
        Assert.Null(a.Country);
        Assert.Equal("https://ember.example.com", a.Website);
        Assert.StartsWith("/uploads/banners/", a.BannerUrl);
        Assert.True(await _app.FileExists(a.BannerUrl!));

        await s.Post("/studio/profile", new Dictionary<string, string> { ["Form.StageName"] = "Black Ember Live", ["Form.Website"] = "", ["Form.RemoveBanner"] = "true" });
        var after = await _app.Db(db => db.ArtistProfiles.AsNoTracking().FirstAsync(x => x.User.Username == "blackember"));
        Assert.Null(after.BannerUrl);
        Assert.Null(after.Website);
        Assert.False(await _app.FileExists(a.BannerUrl!));
    }

    [Theory]
    [InlineData("", "https://x.example.com", "Stage name is required.")]
    [InlineData("Lumen", "not a url", "Enter a full URL, like https://example.com.")]
    public async Task Profile_validation(string stage, string website, string error)
    {
        var s = await _app.LoginAs("lumen@wavelength.app");
        var res = await s.Post("/studio/profile", new Dictionary<string, string> { ["Form.StageName"] = stage, ["Form.Website"] = website });
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains(error, res.Text);
        Assert.Equal("Lumen", await _app.Db(db => db.ArtistProfiles.Where(x => x.User.Username == "lumen").Select(x => x.StageName).FirstAsync()));
    }

    [Fact]
    public async Task Bad_banner_is_refused()
    {
        var s = await _app.LoginAs("hollowkeys@wavelength.app");
        var res = await s.PostMultipart("/studio/profile",
            T2Files.Form(("Form.StageName", "The Hollow Keys")).File("Form.Banner", "b.gif", "text/plain", new byte[] { 1 }));
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains("That file does not look like an image.", res.Text);
    }
}
