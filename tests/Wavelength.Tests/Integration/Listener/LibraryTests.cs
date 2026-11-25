using System.Net;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Integration.Listener;

/// <summary>Listener pages 1, 4, 5 and 6: library tabs, listening history, following, notifications.</summary>
[Collection("T2 uploads")]
public class LibraryTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public LibraryTests(WavelengthFactory app) => _app = app;

    private Task<int> UserId(string username) => _app.Db(db => db.Users.Where(u => u.Username == username).Select(u => u.Id).FirstAsync());

    // ---------- library tabs ----------

    [Fact]
    public async Task Liked_tab_lists_published_liked_tracks_only()
    {
        var me = await UserId("listener");
        var liked = await _app.Db(db => db.TrackLikes.Where(l => l.UserId == me && l.Track.Status == TrackStatus.Published).Select(l => l.Track.Title).ToListAsync());
        var s = await _app.LoginAs("listener@wavelength.app");

        var page = await s.Get("/library");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.NotEmpty(liked);
        foreach (var t in liked) Assert.Contains(t, page.Text);

        // an unknown tab falls back to liked
        Assert.Contains(liked[0], (await s.Get("/library?tab=whatever")).Text);
    }

    [Fact]
    public async Task Albums_tab_lists_saved_albums()
    {
        var s = await _app.LoginAs("listener@wavelength.app");
        var page = await s.Get("/library?tab=albums");
        Assert.Contains("Night Transit", page.Text);
        Assert.Contains("Rainy Window Tapes", page.Text);
        Assert.DoesNotContain("Chrome Hearts", page.Text);
    }

    [Fact]
    public async Task Playlists_tab_lists_saved_public_playlists_but_not_private_ones_of_others()
    {
        var me = await UserId("listener");
        await _app.Db(async db =>
        {
            var secret = await db.Playlists.FirstAsync(p => p.Title == "Secret Guilty Pleasures");
            db.SavedPlaylists.Add(new SavedPlaylist { UserId = me, PlaylistId = secret.Id });
            await db.SaveChangesAsync();
        });
        var s = await _app.LoginAs("listener@wavelength.app");
        var page = await s.Get("/library?tab=playlists");
        Assert.Contains("Chrome & Neon", page.Text);
        Assert.Contains("Yellow Mornings", page.Text);
        Assert.DoesNotContain("Secret Guilty Pleasures", page.Text);
    }

    [Fact]
    public async Task My_playlists_lists_own_playlists_including_private()
    {
        var s = await _app.LoginAs("batikan@wavelength.app");
        var page = await s.Get("/library/playlists");
        Assert.Contains("Yellow Mornings", page.Text);
        Assert.Contains("Secret Guilty Pleasures", page.Text);
        Assert.DoesNotContain("Late Night Coding", page.Text);
    }

    // ---------- history ----------

    private async Task<int> SeedLeoHistory()
    {
        var leo = await UserId("leo");
        await _app.Db(async db =>
        {
            await db.ListeningHistory.Where(h => h.UserId == leo).ExecuteDeleteAsync();
            var lofi = await db.Tracks.FirstAsync(t => t.Title == "Coffee Cloud");
            var rock = await db.Tracks.FirstAsync(t => t.Title == "Rust & Thunder");
            var old = await db.Tracks.FirstAsync(t => t.Title == "Northern Drift");
            db.ListeningHistory.AddRange(
                new ListeningHistory { UserId = leo, TrackId = lofi.Id, PlayedAt = DateTime.UtcNow.AddMinutes(-5), SecondsListened = 60 },
                new ListeningHistory { UserId = leo, TrackId = rock.Id, PlayedAt = DateTime.UtcNow.AddDays(-10), SecondsListened = 45 },
                new ListeningHistory { UserId = leo, TrackId = old.Id, PlayedAt = DateTime.UtcNow.AddDays(-40), SecondsListened = 30 });
            await db.SaveChangesAsync();
        });
        return leo;
    }

    [Fact]
    public async Task History_filters_by_range_and_genre()
    {
        await SeedLeoHistory();
        var s = await _app.LoginAs("leo@example.com");

        var all = await s.Get("/history");
        Assert.Contains("Coffee Cloud", all.Text);
        Assert.Contains("Rust & Thunder", all.Text);
        Assert.Contains("Northern Drift", all.Text);

        var today = await s.Get("/history?range=today");
        Assert.Contains("Coffee Cloud", today.Text);
        Assert.DoesNotContain("Rust & Thunder", today.Text);

        var month = await s.Get("/history?range=month");
        Assert.Contains("Rust & Thunder", month.Text);
        Assert.DoesNotContain("Northern Drift", month.Text);

        var rock = await s.Get("/history?genre=rock");
        Assert.Contains("Rust & Thunder", rock.Text);
        Assert.DoesNotContain("Coffee Cloud", rock.Text);

        // out-of-range page numbers are clamped
        Assert.Equal(HttpStatusCode.OK, (await s.Get("/history?page=999&range=bogus")).Status);
    }

    [Fact]
    public async Task Clearing_history_deletes_only_my_rows()
    {
        var leo = await SeedLeoHistory();
        var othersBefore = await _app.Db(db => db.ListeningHistory.CountAsync(h => h.UserId != leo));
        var s = await _app.LoginAs("leo@example.com");

        var res = await s.Post("/history/clear", new { });
        Assert.Equal(HttpStatusCode.Redirect, res.Status);
        Assert.Equal("/history", res.Location);
        Assert.Equal(0, await _app.Db(db => db.ListeningHistory.CountAsync(h => h.UserId == leo)));
        Assert.Equal(othersBefore, await _app.Db(db => db.ListeningHistory.CountAsync(h => h.UserId != leo)));
        Assert.Contains("Cleared 3 plays", (await s.Get("/history")).Text);

        // clearing again reports an empty history
        await s.Post("/history/clear", new { });
        Assert.Contains("already empty", (await s.Get("/history")).Text);
    }

    // ---------- following ----------

    [Fact]
    public async Task Following_and_followers_tabs()
    {
        var me = await UserId("listener");
        var following = await _app.Db(db => db.Follows.Where(f => f.FollowerId == me)
            .Select(f => f.Followed.ArtistProfile != null ? f.Followed.ArtistProfile.StageName : f.Followed.DisplayName).ToListAsync());
        var s = await _app.LoginAs("listener@wavelength.app");

        var page = await s.Get("/following");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        foreach (var name in following) Assert.Contains(name, page.Text);

        var followers = await s.Get("/following?tab=followers");
        Assert.Contains("İbrahim Mutlu", followers.Text); // ibrahim follows the demo listener
    }

    // ---------- notifications ----------

    [Fact]
    public async Task Notifications_list_and_unread_filter()
    {
        var s = await _app.LoginAs("listener@wavelength.app");
        var all = await s.Get("/notifications");
        Assert.Contains("İbrahim Mutlu started following you.", all.Text);
        Assert.Contains("Welcome to Wavelength", all.Text);

        var unread = await s.Get("/notifications?filter=unread");
        Assert.Contains("started following you", unread.Text);
        Assert.DoesNotContain("Welcome to Wavelength", unread.Text); // welcome is seeded as read
        Assert.Equal(HttpStatusCode.OK, (await s.Get("/notifications?page=50")).Status);
    }

    [Fact]
    public async Task Mark_one_read_and_open()
    {
        var me = await UserId("emre");
        var ids = await _app.Db(async db =>
        {
            var a = new Notification { UserId = me, Type = "follow", Payload = "{\"name\":\"Leo\",\"username\":\"leo\"}" };
            var b = new Notification { UserId = me, Type = "track_approved", Payload = "{\"title\":\"X\",\"track_id\":77}" };
            db.Notifications.AddRange(a, b);
            await db.SaveChangesAsync();
            return (A: a.Id, B: b.Id);
        });
        var s = await _app.LoginAs("emre@wavelength.app");

        var read = await s.Post($"/notifications/{ids.A}/read", new { returnUrl = "/library" });
        Assert.Equal("/library", read.Location);
        Assert.True(await _app.Db(db => db.Notifications.Where(n => n.Id == ids.A).Select(n => n.IsRead).FirstAsync()));

        // external return urls are ignored
        var ext = await s.Post($"/notifications/{ids.A}/read", new { returnUrl = "https://evil.example.com" });
        Assert.Equal("/notifications", ext.Location);

        var open = await s.Post($"/notifications/{ids.B}/open", new { });
        Assert.Equal("/tracks/77", open.Location);
        Assert.True(await _app.Db(db => db.Notifications.Where(n => n.Id == ids.B).Select(n => n.IsRead).FirstAsync()));
    }

    [Fact]
    public async Task Cannot_touch_someone_elses_notification()
    {
        var other = await UserId("maya");
        var id = await _app.Db(async db =>
        {
            var n = new Notification { UserId = other, Type = "welcome", Payload = "{}" };
            db.Notifications.Add(n);
            await db.SaveChangesAsync();
            return n.Id;
        });
        var s = await _app.LoginAs("deniz@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/notifications/{id}/read", new { })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/notifications/{id}/open", new { })).Status);
        Assert.False(await _app.Db(db => db.Notifications.Where(n => n.Id == id).Select(n => n.IsRead).FirstAsync()));
    }

    [Fact]
    public async Task Read_all_marks_every_unread_notification()
    {
        var me = await UserId("huseyin");
        await _app.Db(async db =>
        {
            db.Notifications.Add(new Notification { UserId = me, Type = "welcome", Payload = "{}" });
            db.Notifications.Add(new Notification { UserId = me, Type = "welcome", Payload = "{}" });
            await db.SaveChangesAsync();
        });
        var s = await _app.LoginAs("huseyin@wavelength.app");
        var res = await s.Post("/notifications/read-all", new { });
        Assert.Equal("/notifications", res.Location);
        Assert.Equal(0, await _app.Db(db => db.Notifications.CountAsync(n => n.UserId == me && !n.IsRead)));
        Assert.Contains("Marked", (await s.Get("/notifications")).Text);

        await s.Post("/notifications/read-all", new { });
        Assert.Contains("Nothing new to mark", (await s.Get("/notifications")).Text);
    }
}
