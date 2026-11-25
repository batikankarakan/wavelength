using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Integration.Public;

/// <summary>JSON endpoints behind the player (sequence diagram 4.3) and widgets.</summary>
public class ApiTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public ApiTests(WavelengthFactory app) => _app = app;

    private Task<int> TrackId(string title) => _app.Db(db => db.Tracks.Where(t => t.Title == title).Select(t => t.Id).SingleAsync());

    [Fact]
    public async Task Published_track_is_returned_to_anyone()
    {
        var id = await TrackId("Neon Highway");
        var res = await _app.NewSession().Client.GetAsync($"/api/tracks/{id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, json.GetProperty("id").GetInt32());
        Assert.Equal("Neon Highway", json.GetProperty("title").GetString());
        Assert.Equal("Kora Vale", json.GetProperty("artist").GetString());
        Assert.Equal("/media/audio/neon-highway.mp3", json.GetProperty("audioUrl").GetString());
        Assert.Equal(64, json.GetProperty("durationSec").GetInt32());
        Assert.StartsWith("#", json.GetProperty("color").GetString());
        Assert.StartsWith("#", json.GetProperty("onColor").GetString());
    }

    [Fact]
    public async Task Unpublished_track_is_hidden_from_public_but_visible_to_staff_and_owner()
    {
        var pending = await TrackId("After Hours Club"); // by Kora Vale
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewSession().Client.GetAsync($"/api/tracks/{pending}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await _app.LoginAs("listener@wavelength.app")).Client.GetAsync($"/api/tracks/{pending}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await _app.LoginAs("mio@wavelength.app")).Client.GetAsync($"/api/tracks/{pending}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await _app.LoginAs("kora@wavelength.app")).Client.GetAsync($"/api/tracks/{pending}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await _app.LoginAs("mod@wavelength.app")).Client.GetAsync($"/api/tracks/{pending}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await _app.LoginAs("admin@wavelength.app")).Client.GetAsync($"/api/tracks/{pending}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewSession().Client.GetAsync("/api/tracks/999999")).StatusCode);
    }

    private Task<(int Plays, int History)> Counts(int trackId) => _app.Db(async db =>
        (await db.Tracks.Where(t => t.Id == trackId).Select(t => t.PlaysCount).SingleAsync(), await db.ListeningHistory.CountAsync(h => h.TrackId == trackId)));

    [Fact]
    public async Task Play_by_logged_in_user_writes_history_and_increments_counter()
    {
        var id = await TrackId("Coffee Cloud");
        var before = await Counts(id);
        var s = await _app.LoginAs("listener@wavelength.app");
        var res = await s.PostJson("/api/plays", new { trackId = id, seconds = 31 });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var after = await Counts(id);
        Assert.Equal(before.Plays + 1, after.Plays);
        Assert.Equal(before.History + 1, after.History);
        var row = await _app.Db(db => db.ListeningHistory.Where(h => h.TrackId == id).OrderByDescending(h => h.Id).Include(h => h.User).FirstAsync());
        Assert.Equal("listener", row.User.Username);
        Assert.Equal(31, row.SecondsListened);
        Assert.True(row.PlayedAt > DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task Play_by_visitor_only_increments_counter()
    {
        var id = await TrackId("Static Bloom");
        var before = await Counts(id);
        var res = await _app.NewSession().PostJson("/api/plays", new { trackId = id, seconds = 40 });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var after = await Counts(id);
        Assert.Equal(before.Plays + 1, after.Plays);
        Assert.Equal(before.History, after.History);
    }

    [Fact]
    public async Task Play_seconds_are_clamped_to_track_length()
    {
        const int sent = 99999;
        var id = await TrackId("Honey Tape"); // 52 seconds
        var s = await _app.LoginAs("maya@example.com");
        await s.PostJson("/api/plays", new { trackId = id, seconds = sent });
        await s.PostJson("/api/plays", new { trackId = id, seconds = -20 });
        var rows = await _app.Db(db => db.ListeningHistory.Where(h => h.TrackId == id && h.User.Username == "maya").OrderByDescending(h => h.Id).Take(2).Select(h => h.SecondsListened).ToListAsync());
        Assert.Equal(new[] { 0, 52 }, rows);
    }

    [Fact]
    public async Task Play_of_unpublished_or_missing_track_is_404_and_changes_nothing()
    {
        var pending = await TrackId("Moth to the Flame");
        var before = await Counts(pending);
        var s = await _app.LoginAs("listener@wavelength.app");
        Assert.Equal(HttpStatusCode.NotFound, (await s.PostJson("/api/plays", new { trackId = pending, seconds = 30 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.PostJson("/api/plays", new { trackId = 999999, seconds = 30 })).StatusCode);
        Assert.Equal(before, await Counts(pending));
    }

    [Fact]
    public async Task Play_without_csrf_header_is_rejected()
    {
        var id = await TrackId("Neon Highway");
        var res = await _app.NewSession().Client.PostAsJsonAsync("/api/plays", new { trackId = id, seconds = 30 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Like_toggles_and_reports_count()
    {
        var id = await TrackId("Glass Elevator");
        var s = await _app.LoginAs("leo@example.com");
        await _app.Db(db => db.TrackLikes.Where(l => l.TrackId == id && l.User.Username == "leo").ExecuteDeleteAsync());
        var baseline = await _app.Db(db => db.TrackLikes.CountAsync(l => l.TrackId == id));

        var first = await (await s.PostJson($"/api/tracks/{id}/like")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(first.GetProperty("liked").GetBoolean());
        Assert.Equal(baseline + 1, first.GetProperty("likes").GetInt32());
        Assert.True(await _app.Db(db => db.TrackLikes.AnyAsync(l => l.TrackId == id && l.User.Username == "leo")));

        var second = await (await s.PostJson($"/api/tracks/{id}/like")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(second.GetProperty("liked").GetBoolean());
        Assert.Equal(baseline, second.GetProperty("likes").GetInt32());
    }

    [Fact]
    public async Task Like_requires_login_and_a_published_track()
    {
        var id = await TrackId("Neon Highway");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewSession().PostJson($"/api/tracks/{id}/like")).StatusCode);
        var pending = await TrackId("After Hours Club");
        Assert.Equal(HttpStatusCode.NotFound, (await (await _app.LoginAs("listener@wavelength.app")).PostJson($"/api/tracks/{pending}/like")).StatusCode);
    }

    [Fact]
    public async Task Search_returns_only_published_matches()
    {
        var c = _app.NewSession().Client;
        var neon = await c.GetFromJsonAsync<JsonElement[]>("/api/tracks/search?q=neon");
        Assert.Contains(neon!, t => t.GetProperty("title").GetString() == "Neon Highway");
        Assert.All(neon!, t => Assert.Contains("neon", (t.GetProperty("title").GetString() + t.GetProperty("artist").GetString()).ToLower()));

        var byArtist = await c.GetFromJsonAsync<JsonElement[]>("/api/tracks/search?q=Mio");
        Assert.Equal(3, byArtist!.Length);

        Assert.Empty((await c.GetFromJsonAsync<JsonElement[]>("/api/tracks/search?q=After Hours"))!);
        var popular = await c.GetFromJsonAsync<JsonElement[]>("/api/tracks/search");
        Assert.Equal(12, popular!.Length);
    }

    [Fact]
    public async Task Unread_notification_count_matches_database()
    {
        var s = await _app.LoginAs("listener@wavelength.app");
        var expected = await _app.Db(db => db.Notifications.CountAsync(n => n.User.Username == "listener" && !n.IsRead));
        Assert.True(expected > 0);
        var json = await s.Client.GetFromJsonAsync<JsonElement>("/api/notifications/unread");
        Assert.Equal(expected, json.GetProperty("count").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewSession().Client.GetAsync("/api/notifications/unread")).StatusCode);
    }
}
