using System.Net;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;
using static Wavelength.Tests.Integration.Admin.AdminHelpers;

namespace Wavelength.Tests.Integration.Admin;

/// <summary>Admin pages 5-7: genre management, contact inbox, audit log.</summary>
public class GenresMessagesAuditTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public GenresMessagesAuditTests(WavelengthFactory app) => _app = app;

    private Task<Genre?> Genre(string name) => _app.Db(db => db.Genres.AsNoTracking().SingleOrDefaultAsync(g => g.Name == name));

    // ---------- genres ----------

    [Fact]
    public async Task Create_genre_generates_the_slug_and_uppercases_the_color()
    {
        var admin = await _app.LoginAs("admin@wavelength.app");
        var res = await admin.Post("/admin/genres", F(("NewGenre.Name", "  Drum & Bass "), ("NewGenre.Color", "#a1b2c3")));
        Assert.Contains("created", await admin.Flash(res));
        var g = await Genre("Drum & Bass");
        Assert.NotNull(g);
        Assert.Equal("drum-bass", g!.Slug);
        Assert.Equal("#A1B2C3", g.Color);
        Assert.Contains(await _app.Audit("create_genre"), a => a.Target == "Drum & Bass");
        Assert.Contains("Drum &amp; Bass", (await admin.Get("/admin/genres")).Html);
        Assert.Equal(HttpStatusCode.OK, (await _app.NewSession().Get("/genres/drum-bass")).Status);
    }

    [Theory]
    [InlineData("X", "#FFFFFF", "2 to 40 characters")]
    [InlineData("Trap", "red", "Pick a color")]
    [InlineData("Trap", "#12345", "Pick a color")]
    [InlineData("!!!", "#FFFFFF", "at least one letter")]
    [InlineData("Jazz", "#FFFFFF", "already exists")]
    [InlineData("hip hop", "#FFFFFF", "already exists")] // same slug as Hip-Hop
    public async Task Create_genre_validates(string name, string color, string error)
    {
        var admin = await _app.LoginAs("admin@wavelength.app");
        var count = await _app.Db(db => db.Genres.CountAsync());
        var res = await admin.Post("/admin/genres", F(("NewGenre.Name", name), ("NewGenre.Color", color)));
        Assert.Contains(error, await admin.Flash(res));
        Assert.Equal(count, await _app.Db(db => db.Genres.CountAsync()));
    }

    [Fact]
    public async Task Update_genre_renames_and_recolors()
    {
        var admin = await _app.LoginAs("admin@wavelength.app");
        var id = (await Genre("Synthwave"))!.Id;
        Assert.Contains("saved", await admin.Flash(await admin.Post($"/admin/genres/{id}", F(("name", "Retrowave"), ("color", "#ff00aa")))));
        var g = await _app.Db(db => db.Genres.AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.Equal(("Retrowave", "retrowave", "#FF00AA"), (g.Name, g.Slug, g.Color));
        Assert.Contains(await _app.Audit("update_genre"), a => a.Target == "Synthwave -> Retrowave");

        // recolor only, keeping its own name, is allowed
        await admin.Post($"/admin/genres/{id}", F(("name", "Retrowave"), ("color", "#00FF00")));
        Assert.Equal("#00FF00", (await Genre("Retrowave"))!.Color);
        Assert.Contains(await _app.Audit("update_genre"), a => a.Target == "Retrowave (#00FF00)");

        // renaming onto another genre is refused
        Assert.Contains("already exists", await admin.Flash(await admin.Post($"/admin/genres/{id}", F(("name", "Pop"), ("color", "#00FF00")))));
        Assert.NotNull(await Genre("Retrowave"));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post("/admin/genres/999999", F(("name", "Nope"), ("color", "#000000")))).Status);
    }

    [Fact]
    public async Task Delete_genre_is_refused_while_tracks_use_it()
    {
        var admin = await _app.LoginAs("admin@wavelength.app");
        var rock = (await Genre("Rock"))!;
        var used = await _app.Db(db => db.TrackGenres.CountAsync(x => x.GenreId == rock.Id));
        var res = await admin.Post($"/admin/genres/{rock.Id}/delete");
        Assert.Contains($"used by {used} tracks", await admin.Flash(res));
        Assert.NotNull(await Genre("Rock"));

        await admin.Post("/admin/genres", F(("NewGenre.Name", "Polka"), ("NewGenre.Color", "#123456")));
        var polka = (await Genre("Polka"))!;
        Assert.Contains("deleted", await admin.Flash(await admin.Post($"/admin/genres/{polka.Id}/delete")));
        Assert.Null(await Genre("Polka"));
        Assert.Contains(await _app.Audit("delete_genre"), a => a.Target == "Polka");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post($"/admin/genres/{polka.Id}/delete")).Status);
    }

    [Fact]
    public async Task Delete_genre_used_by_one_track_uses_singular_wording()
    {
        var admin = await _app.LoginAs("admin@wavelength.app");
        await admin.Post("/admin/genres", F(("NewGenre.Name", "Grime"), ("NewGenre.Color", "#123456")));
        await _app.Db(async db =>
        {
            var g = await db.Genres.SingleAsync(x => x.Name == "Grime");
            var t = await db.Tracks.SingleAsync(x => x.Title == "Concrete Crown");
            db.TrackGenres.Add(new TrackGenre { GenreId = g.Id, TrackId = t.Id });
            await db.SaveChangesAsync();
        });
        var id = (await Genre("Grime"))!.Id;
        Assert.Contains("used by 1 track and", await admin.Flash(await admin.Post($"/admin/genres/{id}/delete")));
    }

    // ---------- messages ----------

    [Fact]
    public async Task Inbox_filters_by_status_and_opening_marks_read()
    {
        var admin = await _app.LoginAs("admin@wavelength.app");
        var newOnes = await admin.Get("/admin/messages?status=new");
        Assert.Contains("Aylin K.", newOnes.Text);
        Assert.Contains("Sara", newOnes.Text);
        Assert.DoesNotContain("Tom Becker", newOnes.Text);
        Assert.Contains("Jin", (await admin.Get("/admin/messages?status=replied")).Text);
        var all = await admin.Get("/admin/messages?status=whatever");
        Assert.Contains("Tom Becker", all.Text);

        var id = await _app.Db(db => db.ContactMessages.Where(m => m.Name == "Aylin K.").Select(m => m.Id).SingleAsync());
        var open = await admin.Get($"/admin/messages?id={id}");
        Assert.Contains("dark mode", open.Text);
        Assert.Contains("mailto:aylin@example.com", open.Html);
        Assert.Equal("read", await _app.Db(db => db.ContactMessages.Where(m => m.Id == id).Select(m => m.Status).SingleAsync()));
        Assert.Contains(await _app.Audit("update_message"), a => a.Target == $"message #{id}: new -> read");

        // opening again does not log again
        var logs = (await _app.Audit("update_message")).Count;
        await admin.Get($"/admin/messages?id={id}");
        Assert.Equal(logs, (await _app.Audit("update_message")).Count);
        Assert.Equal(HttpStatusCode.OK, (await admin.Get("/admin/messages?id=999999")).Status);
    }

    [Fact]
    public async Task Message_status_can_be_changed()
    {
        var admin = await _app.LoginAs("admin@wavelength.app");
        var id = await _app.Db(db => db.ContactMessages.Where(m => m.Name == "Tom Becker").Select(m => m.Id).SingleAsync());

        var res = await admin.Post($"/admin/messages/{id}/status", F(("status", "replied")));
        Assert.Equal($"/admin/messages?id={id}", res.Location);
        Assert.Equal("replied", await _app.Db(db => db.ContactMessages.Where(m => m.Id == id).Select(m => m.Status).SingleAsync()));
        Assert.Contains(await _app.Audit("update_message"), a => a.Target == $"message #{id}: read -> replied");

        var logs = (await _app.Audit("update_message")).Count;
        await admin.Post($"/admin/messages/{id}/status", F(("status", "replied"))); // unchanged: no log
        Assert.Equal(logs, (await _app.Audit("update_message")).Count);

        Assert.Contains("Unknown status", await admin.Flash(await admin.Post($"/admin/messages/{id}/status", F(("status", "spam")))));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post("/admin/messages/999999/status", F(("status", "read")))).Status);
    }

    // ---------- audit log ----------

    [Fact]
    public async Task Audit_log_filters_by_action_admin_text_and_dates()
    {
        var admin = await _app.LoginAs("admin@wavelength.app");
        static int Rows(Session.Page p) => p.Doc.QuerySelectorAll("table tbody tr").Length;

        var total = await _app.AuditCount();
        Assert.Equal(Math.Min(20, total), Rows(await admin.Get("/admin/audit")));

        var approve = await _app.Db(db => db.AuditLogs.CountAsync(a => a.Action == "approve_track"));
        Assert.Equal(approve, Rows(await admin.Get("/admin/audit?action=approve_track")));

        var adminId = await _app.UserId("admin");
        var byAdmin = await _app.Db(db => db.AuditLogs.CountAsync(a => a.AdminId == adminId));
        Assert.Equal(byAdmin, Rows(await admin.Get($"/admin/audit?adminId={adminId}")));

        var mio = await admin.Get("/admin/audit?q=Mio");
        Assert.Equal(1, Rows(mio));
        Assert.Contains("Mio Tanaka", mio.Text);

        var future = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd");
        Assert.Equal(0, Rows(await admin.Get($"/admin/audit?from={future}")));
        Assert.Contains("No actions match", (await admin.Get($"/admin/audit?from={future}")).Text);
        var past = DateTime.UtcNow.AddDays(-400).ToString("yyyy-MM-dd");
        Assert.Equal(0, Rows(await admin.Get($"/admin/audit?to={past}")));
        var inRange = await _app.Db(db => db.AuditLogs.CountAsync(a => a.CreatedAt >= DateTime.UtcNow.Date.AddDays(-10)));
        Assert.Equal(Math.Min(20, inRange), Rows(await admin.Get($"/admin/audit?from={DateTime.UtcNow.AddDays(-10):yyyy-MM-dd}")));
    }

    [Fact]
    public async Task Audit_log_pages_after_20_rows()
    {
        var adminId = await _app.UserId("admin");
        await _app.Db(async db =>
        {
            for (var i = 0; i < 30; i++) db.AuditLogs.Add(new AuditLog { AdminId = adminId, Action = "bulk_test", Target = $"row {i}" });
            await db.SaveChangesAsync();
        });
        var admin = await _app.LoginAs("admin@wavelength.app");
        Assert.Equal(20, (await admin.Get("/admin/audit?action=bulk_test")).Doc.QuerySelectorAll("table tbody tr").Length);
        Assert.Equal(10, (await admin.Get("/admin/audit?action=bulk_test&page=2")).Doc.QuerySelectorAll("table tbody tr").Length);
    }

    [Fact]
    public async Task Every_admin_action_writes_an_audit_row()
    {
        var admin = await _app.LoginAs("admin@wavelength.app");
        var maya = await _app.UserId("maya");
        var msg = await _app.Db(db => db.ContactMessages.Where(m => m.Name == "Sara").Select(m => m.Id).SingleAsync());
        var steps = new (string Url, Dictionary<string, string> Form, string Action)[]
        {
            ($"/admin/users/{maya}/ban", F(), "ban_user"),
            ($"/admin/users/{maya}/ban", F(), "unban_user"),
            ($"/admin/users/{maya}/role", F(("role", "artist")), "change_role"),
            ($"/admin/users/{maya}/verify", F(), "verify_artist"),
            ($"/admin/users/{maya}/verify", F(), "unverify_artist"),
            ("/admin/genres", F(("NewGenre.Name", "Audit Genre"), ("NewGenre.Color", "#000000")), "create_genre"),
            ($"/admin/messages/{msg}/status", F(("status", "replied")), "update_message"),
        };
        foreach (var (url, form, action) in steps)
        {
            var before = (await _app.Audit(action)).Count;
            await admin.Post(url, form);
            Assert.True((await _app.Audit(action)).Count == before + 1, $"{url} should log {action}");
        }
        var adminId = await _app.UserId("admin");
        Assert.All(await _app.Db(db => db.AuditLogs.Where(a => a.CreatedAt > DateTime.UtcNow.AddMinutes(-5) && a.Action != "bulk_test").ToListAsync()),
            a => Assert.Equal(adminId, a.AdminId));
    }
}
