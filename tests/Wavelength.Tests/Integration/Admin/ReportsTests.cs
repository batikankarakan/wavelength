using System.Net;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;
using static Wavelength.Tests.Integration.Admin.AdminHelpers;

namespace Wavelength.Tests.Integration.Admin;

/// <summary>Admin page 3: reports (track / comment / user targets), dismiss, resolve, hide and restore comments.</summary>
public class ReportsTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public ReportsTests(WavelengthFactory app) => _app = app;

    /// <summary>Creates a fresh open report so tests don't depend on each other.</summary>
    private Task<int> NewReport(string reporter, int? trackId = null, int? commentId = null, int? userId = null) =>
        _app.Db(async db =>
        {
            var r = new Report
            {
                ReporterId = await db.Users.Where(u => u.Username == reporter).Select(u => u.Id).SingleAsync(),
                TrackId = trackId, CommentId = commentId, ReportedUserId = userId, Reason = "Test report reason", Status = "open",
            };
            db.Reports.Add(r);
            await db.SaveChangesAsync();
            return r.Id;
        });

    private Task<Report> Report(int id) => _app.Db(db => db.Reports.AsNoTracking().SingleAsync(r => r.Id == id));

    private Task<int> NewComment(string track, string body) => _app.Db(async db =>
    {
        var c = new Comment
        {
            UserId = await db.Users.Where(u => u.Username == "ibrahim").Select(u => u.Id).SingleAsync(),
            TrackId = await db.Tracks.Where(t => t.Title == track).Select(t => t.Id).SingleAsync(),
            Body = body,
        };
        db.Comments.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    });

    [Fact]
    public async Task Tabs_list_reports_by_status()
    {
        var mod = await _app.LoginAs("mod@wavelength.app");
        var open = await mod.Get("/admin/reports");
        Assert.Contains("Spam: advertising followers", open.Text);
        Assert.Contains("only posts spam links", open.Text);
        Assert.DoesNotContain("Cover image is too dark", open.Text);

        var dismissed = await mod.Get("/admin/reports?tab=dismissed");
        Assert.Contains("Cover image is too dark", dismissed.Text);
        Assert.DoesNotContain("Spam: advertising followers", dismissed.Text);

        var bogus = await mod.Get("/admin/reports?tab=bogus&page=-3");
        Assert.Equal(HttpStatusCode.OK, bogus.Status);
        Assert.Contains("Spam: advertising followers", bogus.Text); // falls back to open
        Assert.Equal(HttpStatusCode.OK, (await mod.Get("/admin/reports?tab=resolved")).Status);
    }

    [Fact]
    public async Task Dismiss_closes_the_report_and_tells_the_reporter()
    {
        var track = await _app.TrackId("Honey Tape");
        var id = await NewReport("deniz", trackId: track);
        var mod = await _app.LoginAs("mod@wavelength.app");

        var res = await mod.Post($"/admin/reports/{id}/dismiss");
        Assert.Contains("dismissed", await mod.Flash(res));
        var r = await Report(id);
        Assert.Equal("dismissed", r.Status);
        Assert.Equal(await _app.UserId("moderator"), r.HandledBy);
        Assert.Equal(TrackStatus.Published, (await _app.Track(track)).Status); // content untouched
        Assert.Contains(await _app.Notes(await _app.UserId("deniz"), "report_resolved"), n => n.Payload.Contains("dismissed"));
        Assert.Contains(await _app.Audit("dismiss_report"), a => a.Target == $"report #{id}");

        // a handled report can't be handled again
        var again = await mod.Post($"/admin/reports/{id}/resolve");
        Assert.Contains("already handled", await mod.Flash(again));
        Assert.Equal("dismissed", (await Report(id)).Status);
        Assert.Contains("already handled", await mod.Flash(await mod.Post($"/admin/reports/{id}/dismiss")));
    }

    [Fact]
    public async Task Resolving_a_track_report_hides_the_track_and_notifies_everyone()
    {
        var track = await _app.TrackId("Subway Poems");
        var id = await NewReport("leo", trackId: track);
        var mod = await _app.LoginAs("mod@wavelength.app");

        var res = await mod.Post($"/admin/reports/{id}/resolve");
        Assert.Contains("hidden from the public catalog", await mod.Flash(res));
        Assert.Equal(TrackStatus.Hidden, (await _app.Track(track)).Status);
        var r = await Report(id);
        Assert.Equal("resolved", r.Status);
        Assert.Equal(await _app.UserId("moderator"), r.HandledBy);
        Assert.Single(await _app.Notes(await _app.UserId("djatlas"), "track_hidden"));
        Assert.Contains(await _app.Notes(await _app.UserId("leo"), "report_resolved"), n => n.Payload.Contains("resolved"));
        Assert.Contains(await _app.Audit("hide_track"), a => a.Target.Contains($"#{track}"));
        Assert.Contains(await _app.Audit("resolve_report"), a => a.Target == $"report #{id} (track)");
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewSession().Get($"/tracks/{track}")).Status);

        // the track now appears in the review page's hidden list
        Assert.Contains("Subway Poems", (await mod.Get("/admin/review")).Text);

        // a second report on an already hidden track resolves without another notification
        var id2 = await NewReport("maya", trackId: track);
        await mod.Post($"/admin/reports/{id2}/resolve");
        Assert.Equal("resolved", (await Report(id2)).Status);
        Assert.Single(await _app.Notes(await _app.UserId("djatlas"), "track_hidden"));
    }

    [Fact]
    public async Task Resolving_a_comment_report_hides_the_comment_from_the_track_page()
    {
        var commentId = await NewComment("Velvet Circuit", "BUY CHEAP STREAMS AT spam.example");
        var trackId = await _app.TrackId("Velvet Circuit");
        Assert.Contains("BUY CHEAP STREAMS", (await _app.NewSession().Get($"/tracks/{trackId}")).Text);
        var id = await NewReport("listener", commentId: commentId);
        var mod = await _app.LoginAs("mod@wavelength.app");

        var res = await mod.Post($"/admin/reports/{id}/resolve");
        Assert.Contains("comment is hidden", await mod.Flash(res));
        Assert.True(await _app.Db(db => db.Comments.Where(c => c.Id == commentId).Select(c => c.IsHidden).SingleAsync()));
        Assert.DoesNotContain("BUY CHEAP STREAMS", (await _app.NewSession().Get($"/tracks/{trackId}")).Text);
        Assert.Contains(await _app.Audit("resolve_report"), a => a.Target == $"report #{id} (comment)");

        // hidden comments are listed on the reports page and can be restored
        var reports = await mod.Get("/admin/reports?tab=resolved");
        Assert.Contains("BUY CHEAP STREAMS", reports.Text);
        var restore = await mod.Post($"/admin/comments/{commentId}/restore", F(("returnUrl", $"/tracks/{trackId}")));
        Assert.Equal($"/tracks/{trackId}", restore.Location);
        Assert.False(await _app.Db(db => db.Comments.Where(c => c.Id == commentId).Select(c => c.IsHidden).SingleAsync()));
        Assert.Contains("BUY CHEAP STREAMS", (await _app.NewSession().Get($"/tracks/{trackId}")).Text);
        Assert.Contains(await _app.Audit("restore_comment"), a => a.Target == $"comment #{commentId}");
    }

    [Fact]
    public async Task Hide_and_restore_comment_directly_are_idempotent()
    {
        var commentId = await NewComment("Glass Elevator", "direct moderation target");
        var mod = await _app.LoginAs("mod@wavelength.app");

        var hide = await mod.Post($"/admin/comments/{commentId}/hide");
        Assert.Equal("/admin/reports", hide.Location);
        Assert.True(await _app.Db(db => db.Comments.Where(c => c.Id == commentId).Select(c => c.IsHidden).SingleAsync()));
        var hides = (await _app.Audit("hide_comment")).Count(a => a.Target == $"comment #{commentId}");
        await mod.Post($"/admin/comments/{commentId}/hide"); // already hidden: no new audit row
        Assert.Equal(hides, (await _app.Audit("hide_comment")).Count(a => a.Target == $"comment #{commentId}"));

        await mod.Post($"/admin/comments/{commentId}/restore");
        await mod.Post($"/admin/comments/{commentId}/restore");
        Assert.Single(await _app.Audit("restore_comment"), a => a.Target == $"comment #{commentId}");

        Assert.Equal(HttpStatusCode.NotFound, (await mod.Post("/admin/comments/999999/hide")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await mod.Post("/admin/comments/999999/restore")).Status);
    }

    [Fact]
    public async Task A_moderator_resolving_a_user_report_does_not_ban()
    {
        var target = await _app.UserId("leo");
        var id = await NewReport("deniz", userId: target);
        var mod = await _app.LoginAs("mod@wavelength.app");

        var res = await mod.Post($"/admin/reports/{id}/resolve");
        Assert.Contains("Only an admin can ban", await mod.Flash(res));
        Assert.Equal("resolved", (await Report(id)).Status);
        Assert.True(await _app.Db(db => db.Users.Where(u => u.Id == target).Select(u => u.IsActive).SingleAsync()));
    }

    [Fact]
    public async Task An_admin_resolving_a_user_report_bans_the_user()
    {
        var target = await _app.UserId("spammy");
        var id = await NewReport("ibrahim", userId: target);
        var spammy = await _app.LoginAs("spam@example.com");
        var admin = await _app.LoginAs("admin@wavelength.app");

        var res = await admin.Post($"/admin/reports/{id}/resolve");
        Assert.Contains("is banned", await admin.Flash(res));
        Assert.False(await _app.Db(db => db.Users.Where(u => u.Id == target).Select(u => u.IsActive).SingleAsync()));
        Assert.Equal(await _app.UserId("admin"), (await Report(id)).HandledBy);
        Assert.Contains(await _app.Audit("ban_user"), a => a.Target == "@spammy");
        Assert.Contains(await _app.Audit("resolve_report"), a => a.Target == $"report #{id} (user)");

        // the banned user's session stops working
        var after = await spammy.Get("/library");
        Assert.Equal(HttpStatusCode.Redirect, after.Status);
        Assert.Contains("/login", after.Location);
    }

    [Fact]
    public async Task An_admin_cannot_ban_another_admin_through_a_report()
    {
        var mod = await _app.UserId("moderator");
        await _app.Db(async db => { (await db.Users.FindAsync(mod))!.Role = Roles.Admin; await db.SaveChangesAsync(); });
        try
        {
            var id = await NewReport("leo", userId: mod);
            var admin = await _app.LoginAs("admin@wavelength.app");
            var res = await admin.Post($"/admin/reports/{id}/resolve");
            Assert.Contains("Only an admin can ban", await admin.Flash(res));
            Assert.True(await _app.Db(db => db.Users.Where(u => u.Id == mod).Select(u => u.IsActive).SingleAsync()));
        }
        finally
        {
            await _app.Db(async db => { (await db.Users.FindAsync(mod))!.Role = Roles.Moderator; await db.SaveChangesAsync(); });
        }
    }
}
