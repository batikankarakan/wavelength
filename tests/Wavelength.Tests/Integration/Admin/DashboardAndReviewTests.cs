using System.Net;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;
using static Wavelength.Tests.Integration.Admin.AdminHelpers;

namespace Wavelength.Tests.Integration.Admin;

/// <summary>Admin pages 1-2: dashboard KPIs and the review queue (activity diagram, moderator lane).</summary>
public class DashboardAndReviewTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public DashboardAndReviewTests(WavelengthFactory app) => _app = app;

    [Fact]
    public async Task Dashboard_kpis_match_the_database()
    {
        var expected = await _app.Db(async db => new
        {
            Users = await db.Users.CountAsync(),
            Published = await db.Tracks.CountAsync(t => t.Status == TrackStatus.Published),
            Plays7d = await db.ListeningHistory.CountAsync(h => h.PlayedAt >= DateTime.UtcNow.AddDays(-7)),
            OpenReports = await db.Reports.CountAsync(r => r.Status == "open"),
            Pending = await db.Tracks.CountAsync(t => t.Status == TrackStatus.Pending),
        });
        var page = await (await _app.LoginAs("admin@wavelength.app")).Get("/admin");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var counts = page.Doc.QuerySelectorAll("[data-count]").Select(e => int.Parse(e.GetAttribute("data-count")!)).ToList();
        Assert.Contains(expected.Users, counts);
        Assert.Contains(expected.Published, counts);
        Assert.Contains(expected.OpenReports, counts);
        // plays within 7 days can shift by a row at the boundary between query and render; allow 1
        Assert.Contains(counts, c => Math.Abs(c - expected.Plays7d) <= 1);
        Assert.Contains($"{expected.Pending} tracks to review", page.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{expected.OpenReports} open reports", page.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Moderator_dashboard_hides_admin_only_links()
    {
        var page = await (await _app.LoginAs("mod@wavelength.app")).Get("/admin");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.DoesNotContain("href=\"/admin/messages?status=new\"", page.Html);
        Assert.DoesNotContain("href=\"/admin/audit\"", page.Html);
    }

    [Fact]
    public async Task Review_queue_lists_pending_tracks_oldest_first_and_hidden_tracks()
    {
        await _app.Db(async db =>
        {
            var lumen = await db.ArtistProfiles.SingleAsync(a => a.StageName == "Lumen");
            db.Tracks.Add(new Track { ArtistId = lumen.Id, Title = "Queue Newer", AudioUrl = "/media/audio/honey-tape.mp3", DurationSec = 30, Status = TrackStatus.Pending, CreatedAt = DateTime.UtcNow.AddMinutes(-5) });
            db.Tracks.Add(new Track { ArtistId = lumen.Id, Title = "Queue Older", AudioUrl = "/media/audio/honey-tape.mp3", DurationSec = 30, Status = TrackStatus.Pending, CreatedAt = DateTime.UtcNow.AddDays(-30) });
            (await db.Tracks.SingleAsync(x => x.Title == "Iron Lullaby")).Status = TrackStatus.Hidden;
            await db.SaveChangesAsync();
        });
        var page = await (await _app.LoginAs("mod@wavelength.app")).Get("/admin/review");
        var text = page.Text;
        var older = text.IndexOf("Queue Older", StringComparison.Ordinal);
        var newer = text.IndexOf("Queue Newer", StringComparison.Ordinal);
        Assert.True(older >= 0 && newer >= 0);
        Assert.True(older < newer, "oldest pending track should be first");
        Assert.Contains("Iron Lullaby", text);
        Assert.DoesNotContain("Neon Highway", text); // published tracks are not in the queue
        Assert.NotEmpty(page.Doc.QuerySelectorAll("[data-play]"));
    }

    [Fact]
    public async Task Approve_publishes_notifies_the_artist_and_writes_the_audit_log()
    {
        var id = await _app.TrackId("After Hours Club");
        var artistUser = await _app.UserId("kora");
        var mod = await _app.LoginAs("mod@wavelength.app");

        var res = await mod.Post($"/admin/review/{id}/approve");
        Assert.Equal(HttpStatusCode.Redirect, res.Status);
        Assert.Contains("is live", await mod.Flash(res));

        Assert.Equal(TrackStatus.Published, (await _app.Track(id)).Status);
        var note = Assert.Single(await _app.Notes(artistUser, "track_approved"), n => n.Payload.Contains($"\"track_id\":{id}"));
        Assert.Contains("After Hours Club", note.Payload);
        var log = (await _app.Audit("approve_track")).Last();
        Assert.Equal(await _app.UserId("moderator"), log.AdminId);
        Assert.Contains($"#{id}", log.Target);

        // the public can now play it
        Assert.Equal(HttpStatusCode.OK, (await _app.NewSession().Client.GetAsync($"/api/tracks/{id}")).StatusCode);

        // approving twice is refused and changes nothing
        var before = await _app.AuditCount();
        var again = await mod.Post($"/admin/review/{id}/approve");
        Assert.Contains("no longer waiting", await mod.Flash(again));
        Assert.Equal(before, await _app.AuditCount());
    }

    [Fact]
    public async Task Approve_refuses_tracks_that_are_not_pending()
    {
        var mod = await _app.LoginAs("mod@wavelength.app");
        var draft = await _app.TrackId("Untitled Demo 04");
        var res = await mod.Post($"/admin/review/{draft}/approve");
        Assert.Contains("no longer waiting", await mod.Flash(res));
        Assert.Equal(TrackStatus.Draft, (await _app.Track(draft)).Status);

        var missing = await mod.Post("/admin/review/999999/approve");
        Assert.Equal(HttpStatusCode.Redirect, missing.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    [InlineData("    ok   ")] // trimmed to 2 chars
    public async Task Reject_requires_a_reason_of_5_to_200_characters(string reason)
    {
        var id = await _app.TrackId("Moth to the Flame");
        var mod = await _app.LoginAs("mod@wavelength.app");
        var res = await mod.Post($"/admin/review/{id}/reject", F(("reason", reason)));
        Assert.Contains("5 to 200 characters", await mod.Flash(res));
        Assert.Equal(TrackStatus.Pending, (await _app.Track(id)).Status);
    }

    [Fact]
    public async Task Reject_with_a_too_long_reason_is_refused()
    {
        var id = await _app.TrackId("Moth to the Flame");
        var mod = await _app.LoginAs("mod@wavelength.app");
        await mod.Post($"/admin/review/{id}/reject", F(("reason", new string('x', 201))));
        Assert.Equal(TrackStatus.Pending, (await _app.Track(id)).Status);
    }

    [Fact]
    public async Task Reject_sets_rejected_and_sends_the_reason_to_the_artist()
    {
        await _app.Db(async db =>
        {
            var hk = await db.ArtistProfiles.SingleAsync(a => a.StageName == "The Hollow Keys");
            db.Tracks.Add(new Track { ArtistId = hk.Id, Title = "Reject Me", AudioUrl = "/media/audio/moth-to-the-flame.mp3", DurationSec = 30, Status = TrackStatus.Pending });
            await db.SaveChangesAsync();
        });
        var id = await _app.TrackId("Reject Me");
        var artistUser = await _app.UserId("hollowkeys");
        var mod = await _app.LoginAs("mod@wavelength.app");

        var res = await mod.Post($"/admin/review/{id}/reject", F(("reason", "  The vocals are too quiet.  ")));
        Assert.Contains("rejected", await mod.Flash(res));
        Assert.Equal(TrackStatus.Rejected, (await _app.Track(id)).Status);
        var note = Assert.Single(await _app.Notes(artistUser, "track_rejected"));
        Assert.Contains("\"reason\":\"The vocals are too quiet.\"", note.Payload);
        Assert.Contains((await _app.Audit("reject_track")), a => a.Target.Contains($"#{id}"));

        // rejecting an already rejected track is refused
        var again = await mod.Post($"/admin/review/{id}/reject", F(("reason", "Another reason")));
        Assert.Contains("no longer waiting", await mod.Flash(again));
        Assert.Single(await _app.Notes(artistUser, "track_rejected"));
    }

    [Fact]
    public async Task Restore_publishes_a_hidden_track_and_notifies_the_artist()
    {
        var id = await _app.TrackId("Rust & Thunder");
        await _app.Db(async db => { (await db.Tracks.FindAsync(id))!.Status = TrackStatus.Hidden; await db.SaveChangesAsync(); });
        var artistUser = await _app.UserId("blackember");
        var mod = await _app.LoginAs("mod@wavelength.app");

        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewSession().Get($"/tracks/{id}")).Status);
        var res = await mod.Post($"/admin/tracks/{id}/restore", F(("returnUrl", "/admin/reports")));
        Assert.Equal("/admin/reports", res.Location);
        Assert.Equal(TrackStatus.Published, (await _app.Track(id)).Status);
        Assert.Single(await _app.Notes(artistUser, "track_restored"));
        Assert.Contains(await _app.Audit("restore_track"), a => a.Target.Contains($"#{id}"));
        Assert.Equal(HttpStatusCode.OK, (await _app.NewSession().Get($"/tracks/{id}")).Status);

        // restoring a track that is not hidden is refused, external returnUrl is ignored
        var again = await mod.Post($"/admin/tracks/{id}/restore", F(("returnUrl", "https://evil.example.com")));
        Assert.Equal("/admin/review", again.Location);
        Assert.Contains("not hidden", await mod.Flash(again));
    }

    [Fact]
    public async Task Listeners_cannot_moderate()
    {
        var id = await _app.TrackId("Moth to the Flame");
        var listener = await _app.LoginAs("listener@wavelength.app");
        var res = await listener.Post($"/admin/review/{id}/approve");
        Assert.Contains("/403", res.Location);
        Assert.Equal(TrackStatus.Pending, (await _app.Track(id)).Status);
    }
}
