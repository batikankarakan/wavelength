using System.Net;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Integration.Listener;

/// <summary>Listener page 7: profile, avatar, password and account deletion.</summary>
[Collection("T2 uploads")]
public class SettingsTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public SettingsTests(WavelengthFactory app) => _app = app;

    private Task<User> UserBy(string username) => _app.Db(db => db.Users.AsNoTracking().FirstAsync(u => u.Username == username));

    private static Dictionary<string, string> Profile(string name, string username, string email, string? bio = null) => new()
    {
        ["Profile.DisplayName"] = name, ["Profile.Username"] = username, ["Profile.Email"] = email, ["Profile.Bio"] = bio ?? "",
    };

    [Fact]
    public async Task Settings_page_shows_current_values()
    {
        var s = await _app.LoginAs("maya@example.com");
        var page = await s.Get("/settings");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal("Maya Lin", page.Doc.QuerySelector("#Profile_DisplayName")?.GetAttribute("value"));
    }

    [Fact]
    public async Task Profile_update_saves_and_refreshes_the_nav()
    {
        var s = await _app.LoginAs("deniz@example.com");
        var res = await s.Post("/settings/profile", Profile("Deniz A.", "deniz_a", "  DENIZ.NEW@Example.com ", "  New bio  "));
        Assert.Equal("/settings", res.Location);

        var u = await UserBy("deniz_a");
        Assert.Equal("Deniz A.", u.DisplayName);
        Assert.Equal("deniz.new@example.com", u.Email);
        Assert.Equal("New bio", u.Bio);
        var page = await s.Get("/settings");
        Assert.Contains("Profile saved.", page.Text);
        Assert.Contains("Deniz A.", page.Doc.QuerySelector("header")!.TextContent);

        // empty bio is stored as null
        await s.Post("/settings/profile", Profile("Deniz A.", "deniz_a", "deniz.new@example.com", "   "));
        Assert.Null((await UserBy("deniz_a")).Bio);
    }

    [Theory]
    [InlineData("emre", "leo.unique@example.com", "This username is taken.")]
    [InlineData("leo", "emre@wavelength.app", "An account with this email already exists.")]
    [InlineData("bad name!", "leo.x@example.com", "Use letters, numbers, dots and underscores only.")]
    [InlineData("leo", "not-an-email", "Email")]
    public async Task Profile_validation(string username, string email, string error)
    {
        var s = await _app.LoginAs("leo@example.com");
        var res = await s.Post("/settings/profile", Profile("Leo Brandt", username, email));
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains(error, res.Text);
        var leo = await UserBy("leo");
        Assert.Equal("leo@example.com", leo.Email);
    }

    [Fact]
    public async Task Avatar_upload_replace_and_remove()
    {
        var s = await _app.LoginAs("ibrahim@wavelength.app");
        MultipartFormDataContent Form() => T2Files.Form(("Profile.DisplayName", "İbrahim Mutlu"), ("Profile.Username", "ibrahim"), ("Profile.Email", "ibrahim@wavelength.app"));

        await s.PostMultipart("/settings/profile", Form().File("avatar", "me.png", "image/png", T2Files.Png));
        var first = (await UserBy("ibrahim")).AvatarUrl;
        Assert.StartsWith("/uploads/avatars/", first);
        Assert.True(await _app.FileExists(first!));

        await s.PostMultipart("/settings/profile", Form().File("avatar", "me2.png", "image/png", T2Files.Png));
        var second = (await UserBy("ibrahim")).AvatarUrl;
        Assert.NotEqual(first, second);
        Assert.False(await _app.FileExists(first!)); // old file deleted

        var bad = await s.PostMultipart("/settings/profile", Form().File("avatar", "me.svg", "image/svg+xml", new byte[] { 60, 115 }));
        Assert.Equal(HttpStatusCode.OK, bad.Status);
        Assert.Contains("Images must be JPG, PNG, WEBP or GIF.", bad.Text);
        Assert.Equal(second, (await UserBy("ibrahim")).AvatarUrl);

        var f = Form(); f.Add(new StringContent("true"), "Profile.RemoveAvatar");
        await s.PostMultipart("/settings/profile", f);
        Assert.Null((await UserBy("ibrahim")).AvatarUrl);
        Assert.False(await _app.FileExists(second!));
    }

    [Fact]
    public async Task Password_change_rules()
    {
        var s = await _app.LoginAs("huseyin@wavelength.app");

        var wrong = await s.Post("/settings/password", new Dictionary<string, string>
        { ["Password.CurrentPassword"] = "nope-nope", ["Password.NewPassword"] = "BrandNew123", ["Password.ConfirmPassword"] = "BrandNew123" });
        Assert.Equal(HttpStatusCode.OK, wrong.Status);
        Assert.Contains("Current password is wrong.", wrong.Text);

        var mismatch = await s.Post("/settings/password", new Dictionary<string, string>
        { ["Password.CurrentPassword"] = Seeder.DemoPassword, ["Password.NewPassword"] = "BrandNew123", ["Password.ConfirmPassword"] = "Different1" });
        Assert.Contains("Passwords do not match.", mismatch.Text);

        var ok = await s.Post("/settings/password", new Dictionary<string, string>
        { ["Password.CurrentPassword"] = Seeder.DemoPassword, ["Password.NewPassword"] = "BrandNew123", ["Password.ConfirmPassword"] = "BrandNew123" });
        Assert.Equal("/settings#password", ok.Location);
        Assert.Equal(HttpStatusCode.OK, (await s.Get("/settings")).Status); // still signed in

        await _app.LoginAs("huseyin@wavelength.app", "BrandNew123");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _app.LoginAs("huseyin@wavelength.app"));
    }

    [Fact]
    public async Task Delete_account_needs_the_exact_username()
    {
        var s = await _app.LoginAs("listener@wavelength.app");
        var res = await s.Post("/settings/delete", new { confirm = "Listener" });
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains("Type your username exactly to confirm.", res.Text);
        Assert.True(await _app.Db(db => db.Users.AnyAsync(u => u.Username == "listener")));
    }

    [Fact]
    public async Task Delete_account_removes_the_user_and_owned_data()
    {
        var me = (await UserBy("batikan")).Id;
        var s = await _app.LoginAs("batikan@wavelength.app");
        var res = await s.Post("/settings/delete", new { confirm = " batikan " });
        Assert.Equal("/", res.Location);

        await _app.Db(async db =>
        {
            Assert.False(await db.Users.AnyAsync(u => u.Id == me));
            Assert.False(await db.Playlists.AnyAsync(p => p.OwnerId == me));
            Assert.False(await db.Follows.AnyAsync(f => f.FollowerId == me || f.FollowedId == me));
            Assert.False(await db.TrackLikes.AnyAsync(l => l.UserId == me));
            Assert.False(await db.ListeningHistory.AnyAsync(h => h.UserId == me));
            Assert.False(await db.Comments.AnyAsync(c => c.UserId == me));
            Assert.False(await db.SavedAlbums.AnyAsync(x => x.UserId == me));
            Assert.False(await db.Notifications.AnyAsync(n => n.UserId == me));
        });

        // logged out
        var after = await s.Get("/library");
        Assert.Equal(HttpStatusCode.Redirect, after.Status);
        Assert.Contains("/login", after.Location);
    }

    [Theory]
    [InlineData("mod@wavelength.app", "moderator")]
    [InlineData("admin@wavelength.app", "admin")]
    public async Task Staff_cannot_delete_their_account(string email, string username)
    {
        var s = await _app.LoginAs(email);
        var res = await s.Post("/settings/delete", new { confirm = username });
        Assert.Equal("/settings#danger", res.Location);
        Assert.True(await _app.Db(db => db.Users.AnyAsync(u => u.Username == username)));
        Assert.Contains("Staff accounts cannot be deleted", (await s.Get("/settings")).Text);
    }
}
