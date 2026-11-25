using System.Net;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Integration.Public;

/// <summary>Comments, share links, follows, reports, contact form and saving albums / playlists.</summary>
public class InteractionTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public InteractionTests(WavelengthFactory app) => _app = app;

    private Task<int> TrackId(string title) => _app.Db(db => db.Tracks.Where(t => t.Title == title).Select(t => t.Id).SingleAsync());
    private Task<int> UserId(string username) => _app.Db(db => db.Users.Where(u => u.Username == username).Select(u => u.Id).SingleAsync());

    // ---------- comments ----------

    [Fact]
    public async Task Comment_is_saved_shown_and_notifies_the_artist()
    {
        var id = await TrackId("Velvet Circuit");
        var s = await _app.LoginAs("listener@wavelength.app");
        var res = await s.Post($"/tracks/{id}/comments", new Dictionary<string, string> { ["body"] = "  This synth line rules.  " });
        Assert.Equal(HttpStatusCode.Redirect, res.Status);
        var c = await _app.Db(db => db.Comments.Where(x => x.TrackId == id).OrderByDescending(x => x.Id).FirstAsync());
        Assert.Equal("This synth line rules.", c.Body);
        Assert.Null(c.ParentId);
        Assert.Equal($"/tracks/{id}#comment-{c.Id}", res.Location);

        var artistUser = await UserId("novadrift");
        var note = await _app.Db(db => db.Notifications.Where(n => n.UserId == artistUser && n.Type == "comment").OrderByDescending(n => n.Id).FirstAsync());
        Assert.Contains($"\"track_id\":{id}", note.Payload);
        Assert.Contains("Demo Listener", note.Payload);

        Assert.Contains("This synth line rules.", (await _app.NewSession().Get($"/tracks/{id}")).Text);
    }

    [Fact]
    public async Task Artist_commenting_on_own_track_does_not_notify_themselves()
    {
        var id = await TrackId("Northern Drift");
        var kora = await UserId("kora");
        var before = await _app.Db(db => db.Notifications.CountAsync(n => n.UserId == kora));
        await (await _app.LoginAs("kora@wavelength.app")).Post($"/tracks/{id}/comments", new Dictionary<string, string> { ["body"] = "Thanks for listening!" });
        Assert.Equal(before, await _app.Db(db => db.Notifications.CountAsync(n => n.UserId == kora)));
    }

    [Fact]
    public async Task Replies_notify_the_parent_author_and_stay_one_level_deep()
    {
        var id = await TrackId("Rust & Thunder");
        var listener = await _app.LoginAs("listener@wavelength.app");
        await listener.Post($"/tracks/{id}/comments", new Dictionary<string, string> { ["body"] = "Top level comment" });
        var top = await _app.Db(db => db.Comments.Where(x => x.TrackId == id && x.Body == "Top level comment").SingleAsync());

        var emre = await _app.LoginAs("emre@wavelength.app");
        await emre.Post($"/tracks/{id}/comments", new Dictionary<string, string> { ["body"] = "A reply", ["parentId"] = top.Id.ToString() });
        var reply = await _app.Db(db => db.Comments.Where(x => x.Body == "A reply").SingleAsync());
        Assert.Equal(top.Id, reply.ParentId);
        Assert.True(await _app.Db(db => db.Notifications.AnyAsync(n => n.UserId == top.UserId && n.Type == "reply" && n.Payload.Contains("Emre Men"))));

        // replying to a reply attaches to the top-level comment
        await listener.Post($"/tracks/{id}/comments", new Dictionary<string, string> { ["body"] = "Reply to reply", ["parentId"] = reply.Id.ToString() });
        Assert.Equal(top.Id, (await _app.Db(db => db.Comments.Where(x => x.Body == "Reply to reply").SingleAsync())).ParentId);

        var page = await _app.NewSession().Get($"/tracks/{id}");
        Assert.Contains("A reply", page.Text);
        Assert.Contains("Reply to reply", page.Text);
    }

    [Fact]
    public async Task Invalid_comments_are_refused()
    {
        var id = await TrackId("Sugar Signal");
        var s = await _app.LoginAs("maya@example.com");
        var before = await _app.Db(db => db.Comments.CountAsync());

        var empty = await s.Post($"/tracks/{id}/comments", new Dictionary<string, string> { ["body"] = "   " });
        Assert.Equal($"/tracks/{id}#comments", empty.Location);
        await s.Post($"/tracks/{id}/comments", new Dictionary<string, string> { ["body"] = new string('x', 1001) });
        Assert.Equal(before, await _app.Db(db => db.Comments.CountAsync()));

        // parent from another track
        var foreignParent = await _app.Db(db => db.Comments.Where(c => c.TrackId != id).Select(c => c.Id).FirstAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post($"/tracks/{id}/comments", new Dictionary<string, string> { ["body"] = "hi", ["parentId"] = foreignParent.ToString() })).Status);

        // unpublished track
        var pending = await TrackId("After Hours Club");
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/tracks/{pending}/comments", new Dictionary<string, string> { ["body"] = "hi" })).Status);

        // visitors must log in
        var anon = _app.NewSession();
        var redirect = await anon.Post($"/tracks/{id}/comments", new Dictionary<string, string> { ["body"] = "hi" });
        Assert.Contains("/login", redirect.Location);
        Assert.Equal(before, await _app.Db(db => db.Comments.CountAsync()));
    }

    [Fact]
    public async Task Hidden_comments_and_banned_authors_are_not_rendered()
    {
        var id = await TrackId("Gravity Wells");
        var page = await _app.NewSession().Get($"/tracks/{id}");
        Assert.Contains("FREE FOLLOWERS", page.Text);

        await _app.Db(db => db.Comments.Where(c => c.Body.StartsWith("FREE FOLLOWERS")).ExecuteUpdateAsync(x => x.SetProperty(c => c.IsHidden, true)));
        Assert.DoesNotContain("FREE FOLLOWERS", (await _app.NewSession().Get($"/tracks/{id}")).Text);

        var sugar = await TrackId("Sugar Signal");
        Assert.Contains("follow 4 follow", (await _app.NewSession().Get($"/tracks/{sugar}")).Text);
        await _app.Db(db => db.Users.Where(u => u.Username == "spammy").ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false)));
        Assert.DoesNotContain("follow 4 follow", (await _app.NewSession().Get($"/tracks/{sugar}")).Text);
        await _app.Db(db => db.Users.Where(u => u.Username == "spammy").ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, true)));
    }

    [Fact]
    public async Task Unpublished_track_page_is_404_for_public_and_previewable_by_owner_and_staff()
    {
        var pending = await TrackId("Moth to the Flame");
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewSession().Get($"/tracks/{pending}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await (await _app.LoginAs("listener@wavelength.app")).Get($"/tracks/{pending}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await (await _app.LoginAs("hollowkeys@wavelength.app")).Get($"/tracks/{pending}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await (await _app.LoginAs("mod@wavelength.app")).Get($"/tracks/{pending}")).Status);
    }

    [Fact]
    public async Task Track_page_shows_details_lyrics_and_own_playlists()
    {
        var id = await TrackId("Neon Highway");
        var page = await (await _app.LoginAs("listener@wavelength.app")).Get($"/tracks/{id}");
        Assert.Contains("Neon Highway", page.Text);
        Assert.Contains("Headlights paint the overpass", page.Text);
        Assert.Contains("Electronic", page.Text);
        Assert.Contains("Late Night Coding", page.Text); // listener's own playlist in the add-to-playlist menu
        Assert.Contains("That drop at 0:40", page.Text);
    }

    // ---------- share links ----------

    [Fact]
    public async Task Share_link_is_created_once_then_reused_and_counts_clicks()
    {
        var id = await TrackId("Tidal Memory");
        var s = await _app.LoginAs("ibrahim@wavelength.app");
        Assert.Equal($"/tracks/{id}#share", (await s.Post($"/tracks/{id}/share")).Location);
        await s.Post($"/tracks/{id}/share");
        var links = await _app.Db(db => db.ShareLinks.Where(l => l.TrackId == id && l.Creator.Username == "ibrahim").ToListAsync());
        var link = Assert.Single(links);
        Assert.Equal(8, link.Token.Length);
        Assert.Equal(0, link.ClickCount);
        Assert.Contains($"/s/{link.Token}", (await s.Get($"/tracks/{id}")).Html);

        var visitor = _app.NewSession();
        var open = await visitor.Get($"/s/{link.Token}");
        Assert.Equal(HttpStatusCode.Redirect, open.Status);
        Assert.Equal($"/tracks/{id}?from=share", open.Location);
        await visitor.Get($"/s/{link.Token}");
        Assert.Equal(2, await _app.Db(db => db.ShareLinks.Where(l => l.Id == link.Id).Select(l => l.ClickCount).SingleAsync()));
    }

    [Fact]
    public async Task Share_requires_published_track_and_dead_links_404()
    {
        var pending = await TrackId("After Hours Club");
        Assert.Equal(HttpStatusCode.NotFound, (await (await _app.LoginAs("listener@wavelength.app")).Post($"/tracks/{pending}/share")).Status);

        // a share link to a track that gets hidden stops working
        var id = await TrackId("Iron Lullaby");
        var s = await _app.LoginAs("leo@example.com");
        await s.Post($"/tracks/{id}/share");
        var token = await _app.Db(db => db.ShareLinks.Where(l => l.TrackId == id && l.Creator.Username == "leo").Select(l => l.Token).SingleAsync());
        await _app.Db(db => db.Tracks.Where(t => t.Id == id).ExecuteUpdateAsync(x => x.SetProperty(t => t.Status, TrackStatus.Hidden)));
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewSession().Get($"/s/{token}")).Status);
        await _app.Db(db => db.Tracks.Where(t => t.Id == id).ExecuteUpdateAsync(x => x.SetProperty(t => t.Status, TrackStatus.Published)));
    }

    // ---------- follows ----------

    [Fact]
    public async Task Follow_toggles_and_notifies()
    {
        var target = await UserId("lumen");
        var s = await _app.LoginAs("leo@example.com");
        var leo = await UserId("leo");
        await _app.Db(db => db.Follows.Where(f => f.FollowerId == leo && f.FollowedId == target).ExecuteDeleteAsync());

        var res = await s.Post($"/follow/{target}", new Dictionary<string, string> { ["returnUrl"] = "/explore" });
        Assert.Equal("/explore", res.Location);
        Assert.True(await _app.Db(db => db.Follows.AnyAsync(f => f.FollowerId == leo && f.FollowedId == target)));
        Assert.True(await _app.Db(db => db.Notifications.AnyAsync(n => n.UserId == target && n.Type == "follow" && n.Payload.Contains("\"username\":\"leo\""))));

        var again = await s.Post($"/follow/{target}", new Dictionary<string, string> { ["returnUrl"] = "https://evil.example.com" });
        Assert.Equal("/", again.Location); // non-local return urls are ignored
        Assert.False(await _app.Db(db => db.Follows.AnyAsync(f => f.FollowerId == leo && f.FollowedId == target)));
    }

    [Fact]
    public async Task Follow_rules_self_missing_and_banned()
    {
        var s = await _app.LoginAs("deniz@example.com");
        var me = await UserId("deniz");
        await s.Post($"/follow/{me}");
        Assert.False(await _app.Db(db => db.Follows.AnyAsync(f => f.FollowerId == me && f.FollowedId == me)));
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post("/follow/999999")).Status);

        var spammy = await UserId("spammy");
        await _app.Db(db => db.Users.Where(u => u.Id == spammy).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false)));
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/follow/{spammy}")).Status);
        await _app.Db(db => db.Users.Where(u => u.Id == spammy).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, true)));

        Assert.Contains("/login", (await _app.NewSession().Post($"/follow/{spammy}")).Location);
    }

    // ---------- reports ----------

    [Fact]
    public async Task Reports_need_exactly_one_target_a_reason_and_no_duplicates()
    {
        var s = await _app.LoginAs("huseyin@wavelength.app");
        var track = await TrackId("Concrete Crown");
        var comment = await _app.Db(db => db.Comments.Where(c => c.Body.StartsWith("Boom bap")).Select(c => c.Id).SingleAsync());
        var me = await UserId("huseyin");
        Task<int> Count() => _app.Db(db => db.Reports.CountAsync(r => r.ReporterId == me));

        await s.Post("/reports", new Dictionary<string, string> { ["reason"] = "No target at all" });
        await s.Post("/reports", new Dictionary<string, string> { ["trackId"] = track.ToString(), ["commentId"] = comment.ToString(), ["reason"] = "Two targets here" });
        await s.Post("/reports", new Dictionary<string, string> { ["trackId"] = track.ToString(), ["reason"] = "bad" });
        await s.Post("/reports", new Dictionary<string, string> { ["trackId"] = track.ToString(), ["reason"] = new string('r', 301) });
        await s.Post("/reports", new Dictionary<string, string> { ["reportedUserId"] = me.ToString(), ["reason"] = "Reporting myself" });
        await s.Post("/reports", new Dictionary<string, string> { ["trackId"] = "999999", ["reason"] = "Missing track" });
        Assert.Equal(0, await Count());

        var ok = await s.Post("/reports", new Dictionary<string, string> { ["trackId"] = track.ToString(), ["reason"] = "  Uses a sample without credit.  ", ["returnUrl"] = $"/tracks/{track}" });
        Assert.Equal($"/tracks/{track}", ok.Location);
        var r = await _app.Db(db => db.Reports.SingleAsync(x => x.ReporterId == me));
        Assert.Equal("Uses a sample without credit.", r.Reason);
        Assert.Equal("open", r.Status);
        Assert.Equal(track, r.TrackId);
        Assert.Null(r.CommentId);
        Assert.Null(r.ReportedUserId);

        await s.Post("/reports", new Dictionary<string, string> { ["trackId"] = track.ToString(), ["reason"] = "Reporting it again" });
        Assert.Equal(1, await Count());

        await s.Post("/reports", new Dictionary<string, string> { ["commentId"] = comment.ToString(), ["reason"] = "Rude comment here" });
        var spammy = await UserId("spammy");
        await s.Post("/reports", new Dictionary<string, string> { ["reportedUserId"] = spammy.ToString(), ["reason"] = "Spam account" });
        Assert.Equal(3, await Count());
    }

    [Fact]
    public async Task Hidden_comments_cannot_be_reported()
    {
        var s = await _app.LoginAs("emre@wavelength.app");
        var comment = await _app.Db(db => db.Comments.Where(c => c.Body.StartsWith("Same here")).Select(c => c.Id).SingleAsync());
        await _app.Db(db => db.Comments.Where(c => c.Id == comment).ExecuteUpdateAsync(x => x.SetProperty(c => c.IsHidden, true)));
        await s.Post("/reports", new Dictionary<string, string> { ["commentId"] = comment.ToString(), ["reason"] = "Hidden already" });
        Assert.False(await _app.Db(db => db.Reports.AnyAsync(r => r.CommentId == comment)));
    }

    // ---------- contact ----------

    [Fact]
    public async Task Contact_form_saves_message()
    {
        var s = _app.NewSession();
        await s.Get("/contact");
        var res = await s.Post("/contact", new Dictionary<string, string>
        {
            ["Name"] = "Ada Lovelace", ["Email"] = " Ada@Example.COM ", ["Topic"] = "feedback", ["Message"] = "The player is lovely, keep going!",
        });
        Assert.Equal("/contact?sent=1", res.Location);
        var m = await _app.Db(db => db.ContactMessages.SingleAsync(x => x.Name == "Ada Lovelace"));
        Assert.Equal("ada@example.com", m.Email);
        Assert.Equal("feedback", m.Topic);
        Assert.Equal("new", m.Status);
        var thanks = await s.Get("/contact?sent=1");
        Assert.Contains("Thanks Ada", thanks.Text);
    }

    [Theory]
    [InlineData("A", "a@example.com", "general", "Long enough message")]
    [InlineData("Valid Name", "nope", "general", "Long enough message")]
    [InlineData("Valid Name", "a@example.com", "general", "short")]
    [InlineData("Valid Name", "a@example.com", "pizza", "Long enough message")]
    public async Task Contact_form_validates_on_the_server(string name, string email, string topic, string message)
    {
        var s = _app.NewSession();
        await s.Get("/contact");
        var before = await _app.Db(db => db.ContactMessages.CountAsync());
        var res = await s.Post("/contact", new Dictionary<string, string> { ["Name"] = name, ["Email"] = email, ["Topic"] = topic, ["Message"] = message });
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Equal(before, await _app.Db(db => db.ContactMessages.CountAsync()));
    }

    // ---------- saving albums and playlists ----------

    [Fact]
    public async Task Album_save_toggles()
    {
        var album = await _app.Db(db => db.Albums.Where(a => a.Title == "Blue Hour").Select(a => a.Id).SingleAsync());
        var s = await _app.LoginAs("maya@example.com");
        var maya = await UserId("maya");
        Assert.Equal($"/albums/{album}", (await s.Post($"/albums/{album}/save")).Location);
        Assert.True(await _app.Db(db => db.SavedAlbums.AnyAsync(x => x.UserId == maya && x.AlbumId == album)));
        Assert.Contains("Blue Hour", (await s.Get("/library?tab=albums")).Text);
        await s.Post($"/albums/{album}/save", new Dictionary<string, string> { ["returnUrl"] = "/library" });
        Assert.False(await _app.Db(db => db.SavedAlbums.AnyAsync(x => x.UserId == maya && x.AlbumId == album)));
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post("/albums/999999/save")).Status);
    }

    [Fact]
    public async Task Playlist_save_toggles_but_not_for_own_or_private_playlists()
    {
        var ids = await _app.Db(async db => new
        {
            Public = await db.Playlists.Where(p => p.Title == "Chrome & Neon").Select(p => p.Id).SingleAsync(),
            Own = await db.Playlists.Where(p => p.Title == "Late Night Coding").Select(p => p.Id).SingleAsync(),
            Private = await db.Playlists.Where(p => p.Title == "Secret Guilty Pleasures").Select(p => p.Id).SingleAsync(),
        });
        var s = await _app.LoginAs("listener@wavelength.app");
        var me = await UserId("listener");
        await _app.Db(db => db.SavedPlaylists.Where(x => x.UserId == me && x.PlaylistId == ids.Public).ExecuteDeleteAsync());

        await s.Post($"/playlists/{ids.Public}/save");
        Assert.True(await _app.Db(db => db.SavedPlaylists.AnyAsync(x => x.UserId == me && x.PlaylistId == ids.Public)));
        await s.Post($"/playlists/{ids.Public}/save");
        Assert.False(await _app.Db(db => db.SavedPlaylists.AnyAsync(x => x.UserId == me && x.PlaylistId == ids.Public)));

        await s.Post($"/playlists/{ids.Own}/save");
        Assert.False(await _app.Db(db => db.SavedPlaylists.AnyAsync(x => x.UserId == me && x.PlaylistId == ids.Own)));

        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/playlists/{ids.Private}/save")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post("/playlists/999999/save")).Status);
    }

    [Fact]
    public async Task Private_playlist_is_only_visible_to_its_owner()
    {
        var id = await _app.Db(db => db.Playlists.Where(p => p.Title == "Secret Guilty Pleasures").Select(p => p.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewSession().Get($"/playlists/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await (await _app.LoginAs("listener@wavelength.app")).Get($"/playlists/{id}")).Status);
        var owner = await (await _app.LoginAs("batikan@wavelength.app")).Get($"/playlists/{id}");
        Assert.Equal(HttpStatusCode.OK, owner.Status);
        Assert.Contains("Secret Guilty Pleasures", owner.Text);

        // and it is not listed on the owner's public profile for others
        var profile = await _app.NewSession().Get("/users/batikan");
        Assert.Contains("Yellow Mornings", profile.Text);
        Assert.DoesNotContain("Secret Guilty Pleasures", profile.Text);
    }

    [Fact]
    public async Task Public_playlist_lists_only_published_tracks_in_order()
    {
        var id = await _app.Db(db => db.Playlists.Where(p => p.Title == "Rainy Window").Select(p => p.Id).SingleAsync());
        var page = await _app.NewSession().Get($"/playlists/{id}");
        var text = page.Text;
        Assert.True(text.IndexOf("Slow Burn Rain", StringComparison.Ordinal) < text.IndexOf("Blue Hour Avenue", StringComparison.Ordinal));
        Assert.Contains("Tidal Memory", text);
    }
}
