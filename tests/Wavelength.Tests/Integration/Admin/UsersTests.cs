using System.Net;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;
using static Wavelength.Tests.Integration.Admin.AdminHelpers;

namespace Wavelength.Tests.Integration.Admin;

/// <summary>Admin page 4: user management (search, filters, ban, roles, artist verification).</summary>
public class UsersTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public UsersTests(WavelengthFactory app) => _app = app;

    private Task<User> U(int id) => _app.Db(db => db.Users.AsNoTracking().Include(u => u.ArtistProfile).SingleAsync(u => u.Id == id));

    private static List<string> Usernames(Session.Page page) =>
        page.Doc.QuerySelectorAll("table tbody tr").Select(tr => tr.TextContent).ToList();

    [Fact]
    public async Task Search_and_filters_narrow_the_list()
    {
        var admin = await _app.LoginAs("admin@wavelength.app");

        var all = await admin.Get("/admin/users");
        Assert.Equal(Math.Min(20, await _app.Db(db => db.Users.CountAsync())), Usernames(all).Count);

        var byEmail = Usernames(await admin.Get("/admin/users?q=spam@example"));
        Assert.Single(byEmail);
        Assert.Contains("spammy", byEmail[0]);

        var byStage = Usernames(await admin.Get("/admin/users?q=Hollow"));
        Assert.Contains(byStage, r => r.Contains("hollowkeys"));

        var artists = Usernames(await admin.Get("/admin/users?role=artist"));
        Assert.Equal(8, artists.Count);

        var ignoredRole = Usernames(await admin.Get("/admin/users?role=superuser"));
        Assert.Equal(Usernames(all).Count, ignoredRole.Count);

        await _app.Db(async db => { (await db.Users.SingleAsync(u => u.Username == "leo")).IsActive = false; await db.SaveChangesAsync(); });
        var banned = Usernames(await admin.Get("/admin/users?status=banned"));
        Assert.Single(banned);
        Assert.Contains("leo", banned[0]);
        Assert.DoesNotContain(Usernames(await admin.Get("/admin/users?status=active")), r => r.Contains("@leo"));
        await _app.Db(async db => { (await db.Users.SingleAsync(u => u.Username == "leo")).IsActive = true; await db.SaveChangesAsync(); });

        Assert.Empty(Usernames(await admin.Get("/admin/users?q=zzz-nobody")));
        Assert.Contains("No users match", (await admin.Get("/admin/users?q=zzz-nobody")).Text);
    }

    [Fact]
    public async Task Users_list_pages_after_20_rows()
    {
        await _app.Db(async db =>
        {
            for (var i = 0; i < 25; i++)
                db.Users.Add(new User { Username = $"bulk{i}", Email = $"bulk{i}@example.com", DisplayName = $"Bulk {i}", PasswordHash = "x" });
            await db.SaveChangesAsync();
        });
        var admin = await _app.LoginAs("admin@wavelength.app");
        var p1 = await admin.Get("/admin/users?q=bulk");
        var p2 = await admin.Get("/admin/users?q=bulk&page=2");
        Assert.Equal(20, Usernames(p1).Count);
        Assert.Equal(5, Usernames(p2).Count);
        Assert.Contains("page=2", p1.Html);
        Assert.Equal(20, Usernames(await admin.Get("/admin/users?q=bulk&page=0")).Count);
    }

    [Fact]
    public async Task Ban_and_unban_toggle_and_kick_the_user_out()
    {
        var target = await _app.UserId("deniz");
        var deniz = await _app.LoginAs("deniz@example.com");
        Assert.Equal(HttpStatusCode.OK, (await deniz.Get("/library")).Status);
        var admin = await _app.LoginAs("admin@wavelength.app");

        var ban = await admin.Post($"/admin/users/{target}/ban", F(("returnUrl", "/admin/users?q=deniz")));
        Assert.Equal("/admin/users?q=deniz", ban.Location);
        Assert.Contains("is banned", await admin.Flash(ban));
        Assert.False((await U(target)).IsActive);
        Assert.Contains(await _app.Audit("ban_user"), a => a.Target == "@deniz");

        // existing session is rejected on the next request, and logging in again fails
        var kicked = await deniz.Get("/library");
        Assert.Equal(HttpStatusCode.Redirect, kicked.Status);
        Assert.Contains("/login", kicked.Location);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _app.LoginAs("deniz@example.com"));

        var unban = await admin.Post($"/admin/users/{target}/ban");
        Assert.Contains("can log in again", await admin.Flash(unban));
        Assert.True((await U(target)).IsActive);
        Assert.Contains(await _app.Audit("unban_user"), a => a.Target == "@deniz");
        await _app.LoginAs("deniz@example.com");
    }

    [Fact]
    public async Task Admins_cannot_be_banned_including_yourself()
    {
        var admin = await _app.LoginAs("admin@wavelength.app");
        var self = await _app.UserId("admin");
        var res = await admin.Post($"/admin/users/{self}/ban");
        Assert.Contains("cannot be banned", await admin.Flash(res));
        Assert.True((await U(self)).IsActive);

        var other = await _app.UserId("ibrahim");
        await _app.Db(async db => { (await db.Users.FindAsync(other))!.Role = Roles.Admin; await db.SaveChangesAsync(); });
        await admin.Post($"/admin/users/{other}/ban");
        Assert.True((await U(other)).IsActive);
        await _app.Db(async db => { (await db.Users.FindAsync(other))!.Role = Roles.Listener; await db.SaveChangesAsync(); });

        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post("/admin/users/999999/ban")).Status);
    }

    [Fact]
    public async Task Moderators_cannot_manage_users()
    {
        var mod = await _app.LoginAs("mod@wavelength.app");
        var target = await _app.UserId("maya");
        Assert.Contains("/403", (await mod.Post($"/admin/users/{target}/ban")).Location);
        Assert.Contains("/403", (await mod.Post($"/admin/users/{target}/role", F(("role", "admin")))).Location);
        Assert.True((await U(target)).IsActive);
        Assert.Equal(Roles.Listener, (await U(target)).Role);
    }

    [Fact]
    public async Task Changing_a_role_to_artist_creates_a_profile_and_the_cookie_follows()
    {
        var target = await _app.UserId("maya");
        var maya = await _app.LoginAs("maya@example.com");
        Assert.Contains("/403", (await maya.Get("/studio")).Location);
        var admin = await _app.LoginAs("admin@wavelength.app");

        var res = await admin.Post($"/admin/users/{target}/role", F(("role", "artist")));
        Assert.Contains("is now artist", await admin.Flash(res));
        var u = await U(target);
        Assert.Equal(Roles.Artist, u.Role);
        Assert.NotNull(u.ArtistProfile);
        Assert.Equal("Maya Lin", u.ArtistProfile!.StageName);
        Assert.Contains(await _app.Notes(target, "role_changed"), n => n.Payload.Contains("artist"));
        Assert.Contains(await _app.Audit("change_role"), a => a.Target == "@maya: listener -> artist");

        // Maya's existing cookie now has the artist role
        Assert.Equal(HttpStatusCode.OK, (await maya.Get("/studio")).Status);

        // back to listener and to artist again: the profile is reused, not duplicated
        await admin.Post($"/admin/users/{target}/role", F(("role", "listener")));
        Assert.Contains("/403", (await maya.Get("/studio")).Location);
        await admin.Post($"/admin/users/{target}/role", F(("role", "artist")));
        Assert.Equal(1, await _app.Db(db => db.ArtistProfiles.CountAsync(a => a.UserId == target)));

        // promoting to moderator opens the moderation pages
        await admin.Post($"/admin/users/{target}/role", F(("role", "moderator")));
        Assert.Equal(HttpStatusCode.OK, (await maya.Get("/admin/review")).Status);
        await admin.Post($"/admin/users/{target}/role", F(("role", "listener")));
    }

    [Fact]
    public async Task Role_change_rejects_unknown_roles_same_role_and_self()
    {
        var admin = await _app.LoginAs("admin@wavelength.app");
        var target = await _app.UserId("leo");
        var before = await _app.AuditCount();

        Assert.Contains("Unknown role", await admin.Flash(await admin.Post($"/admin/users/{target}/role", F(("role", "superuser")))));
        Assert.Equal(Roles.Listener, (await U(target)).Role);

        var same = await admin.Post($"/admin/users/{target}/role", F(("role", "listener")));
        Assert.Equal("/admin/users", same.Location);
        Assert.Equal(before, await _app.AuditCount());

        var self = await _app.UserId("admin");
        Assert.Contains("own role", await admin.Flash(await admin.Post($"/admin/users/{self}/role", F(("role", "listener")))));
        Assert.Equal(Roles.Admin, (await U(self)).Role);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post("/admin/users/999999/role", F(("role", "listener")))).Status);
    }

    [Fact]
    public async Task Verify_toggles_the_artist_badge_and_notifies_once()
    {
        var target = await _app.UserId("lumen");
        var admin = await _app.LoginAs("admin@wavelength.app");
        Assert.False((await U(target)).ArtistProfile!.Verified);

        Assert.Contains("is verified", await admin.Flash(await admin.Post($"/admin/users/{target}/verify")));
        Assert.True((await U(target)).ArtistProfile!.Verified);
        Assert.Single(await _app.Notes(target, "artist_verified"));
        Assert.Contains(await _app.Audit("verify_artist"), a => a.Target == "Lumen");
        var artistId = (await U(target)).ArtistProfile!.Id;
        Assert.Contains("Lumen ✓", (await _app.NewSession().Get($"/artists/{artistId}")).Text.Replace("\n", " "));

        Assert.Contains("no longer verified", await admin.Flash(await admin.Post($"/admin/users/{target}/verify")));
        Assert.False((await U(target)).ArtistProfile!.Verified);
        Assert.Single(await _app.Notes(target, "artist_verified")); // no notification on unverify
        Assert.Contains(await _app.Audit("unverify_artist"), a => a.Target == "Lumen");

        var listener = await _app.UserId("listener");
        Assert.Contains("no artist profile", await admin.Flash(await admin.Post($"/admin/users/{listener}/verify")));
    }
}
