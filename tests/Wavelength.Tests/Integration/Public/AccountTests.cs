using System.Net;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Integration.Public;

/// <summary>Public pages 4-6: sign up, log in / out, forgot and reset password.</summary>
public class AccountTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public AccountTests(WavelengthFactory app) => _app = app;

    private static Dictionary<string, string> Registration(string username, string email, bool artist = false, string? stage = null) => new()
    {
        ["DisplayName"] = "Test Person", ["Username"] = username, ["Email"] = email,
        ["Password"] = "longpassword1", ["ConfirmPassword"] = "longpassword1",
        ["AsArtist"] = artist ? "true" : "false", ["StageName"] = stage ?? "", ["AcceptTerms"] = "true",
    };

    private async Task<Session.Page> Register(Session s, Dictionary<string, string> f)
    {
        await s.Get("/register");
        return await s.Post("/register", f);
    }

    [Fact]
    public async Task Register_listener_creates_account_signs_in_and_sends_welcome()
    {
        var s = _app.NewSession();
        var res = await Register(s, Registration("new_listener", "New.Listener@Example.com"));
        Assert.Equal(HttpStatusCode.Redirect, res.Status);
        Assert.Equal("/explore", res.Location);

        var user = await _app.Db(db => db.Users.SingleAsync(u => u.Username == "new_listener"));
        Assert.Equal("new.listener@example.com", user.Email); // normalized
        Assert.Equal(Roles.Listener, user.Role);
        Assert.True(user.IsActive);
        Assert.DoesNotContain("longpassword1", user.PasswordHash);
        Assert.False(await _app.Db(db => db.ArtistProfiles.AnyAsync(a => a.UserId == user.Id)));
        Assert.True(await _app.Db(db => db.Notifications.AnyAsync(n => n.UserId == user.Id && n.Type == "welcome")));

        // signed in: the library opens
        Assert.Equal(HttpStatusCode.OK, (await s.Get("/library")).Status);
    }

    [Fact]
    public async Task Register_artist_creates_artist_profile_and_opens_studio()
    {
        var s = _app.NewSession();
        var res = await Register(s, Registration("new_artist", "artist.new@example.com", artist: true, stage: "  Night Owls "));
        Assert.Equal("/studio", res.Location);
        var profile = await _app.Db(db => db.ArtistProfiles.Include(a => a.User).SingleAsync(a => a.User.Username == "new_artist"));
        Assert.Equal("Night Owls", profile.StageName);
        Assert.Equal(Roles.Artist, profile.User.Role);
        Assert.False(profile.Verified);
        Assert.Equal(HttpStatusCode.OK, (await s.Get("/studio")).Status);
    }

    [Fact]
    public async Task Register_artist_without_stage_name_is_refused()
    {
        var res = await Register(_app.NewSession(), Registration("no_stage", "nostage@example.com", artist: true));
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains("Artists need a stage name.", res.Text);
        Assert.False(await _app.Db(db => db.Users.AnyAsync(u => u.Username == "no_stage")));
    }

    [Theory]
    [InlineData("listener", "fresh@example.com", "This username is taken.")]
    [InlineData("fresh_name", "LISTENER@wavelength.app", "An account with this email already exists.")]
    public async Task Register_rejects_duplicates(string username, string email, string error)
    {
        var res = await Register(_app.NewSession(), Registration(username, email));
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains(error, res.Text);
        Assert.Equal(1, await _app.Db(db => db.Users.CountAsync(u => u.Username == "listener")));
    }

    [Theory]
    [InlineData("Password", "short", "Password needs at least 8 characters.")]
    [InlineData("ConfirmPassword", "different123", "Passwords do not match.")]
    [InlineData("Username", "bad name!", "Use letters, numbers, dots and underscores only.")]
    [InlineData("Email", "not-an-email", "Enter a valid email address.")]
    [InlineData("AcceptTerms", "false", "Please accept the terms.")]
    public async Task Register_validates_fields(string field, string value, string error)
    {
        var f = Registration("valid_user", "valid@example.com");
        f[field] = value;
        if (field == "Password") f["ConfirmPassword"] = value;
        var res = await Register(_app.NewSession(), f);
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains(error, res.Text);
        Assert.False(await _app.Db(db => db.Users.AnyAsync(u => u.Username == "valid_user")));
    }

    [Fact]
    public async Task Register_and_login_pages_redirect_when_already_signed_in()
    {
        var s = await _app.LoginAs("listener@wavelength.app");
        Assert.Equal("/", (await s.Get("/register")).Location);
        Assert.Equal("/library", (await s.Get("/login?returnUrl=/library")).Location);
    }

    [Fact]
    public async Task Login_with_wrong_password_shows_error()
    {
        var s = _app.NewSession();
        await s.Get("/login");
        var res = await s.Post("/login", new Dictionary<string, string> { ["Email"] = "listener@wavelength.app", ["Password"] = "nope-nope" });
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains("Wrong email or password.", res.Text);
        Assert.Equal(HttpStatusCode.Redirect, (await s.Get("/library")).Status); // still anonymous
    }

    [Fact]
    public async Task Login_with_unknown_email_and_invalid_form()
    {
        var s = _app.NewSession();
        await s.Get("/login");
        Assert.Contains("Wrong email or password.", (await s.Post("/login", new Dictionary<string, string> { ["Email"] = "ghost@example.com", ["Password"] = "x" })).Text);
        var invalid = await s.Post("/login", new Dictionary<string, string> { ["Email"] = "", ["Password"] = "" });
        Assert.Equal(HttpStatusCode.OK, invalid.Status);
    }

    [Fact]
    public async Task Login_is_case_insensitive_and_follows_local_return_url_only()
    {
        var s = _app.NewSession();
        await s.Get("/login");
        var res = await s.Post("/login", new Dictionary<string, string> { ["Email"] = " Listener@Wavelength.App ", ["Password"] = Seeder.DemoPassword, ["ReturnUrl"] = "/history" });
        Assert.Equal("/history", res.Location);

        var s2 = _app.NewSession();
        await s2.Get("/login");
        var evil = await s2.Post("/login", new Dictionary<string, string> { ["Email"] = "listener@wavelength.app", ["Password"] = Seeder.DemoPassword, ["ReturnUrl"] = "https://evil.example.com/" });
        Assert.Equal("/", evil.Location);
    }

    [Fact]
    public async Task Banned_user_cannot_log_in_and_existing_session_is_dropped()
    {
        var active = await _app.LoginAs("leo@example.com");
        Assert.Equal(HttpStatusCode.OK, (await active.Get("/library")).Status);

        await _app.Db(db => db.Users.Where(u => u.Email == "leo@example.com").ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false)));

        var after = await active.Get("/library");
        Assert.Equal(HttpStatusCode.Redirect, after.Status);
        Assert.Contains("/login", after.Location);

        var s = _app.NewSession();
        await s.Get("/login");
        var res = await s.Post("/login", new Dictionary<string, string> { ["Email"] = "leo@example.com", ["Password"] = Seeder.DemoPassword });
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains("suspended", res.Text);

        await _app.Db(db => db.Users.Where(u => u.Email == "leo@example.com").ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, true)));
    }

    [Fact]
    public async Task Role_change_takes_effect_on_the_next_request()
    {
        var s = await _app.LoginAs("maya@example.com");
        Assert.Contains("/403", (await s.Get("/admin")).Location);
        await _app.Db(db => db.Users.Where(u => u.Email == "maya@example.com").ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, Roles.Moderator)));
        Assert.Equal(HttpStatusCode.OK, (await s.Get("/admin")).Status);
        await _app.Db(db => db.Users.Where(u => u.Email == "maya@example.com").ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, Roles.Listener)));
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var s = await _app.LoginAs("listener@wavelength.app");
        await s.Get("/library");
        var res = await s.Post("/logout");
        Assert.Equal("/", res.Location);
        Assert.Equal(HttpStatusCode.Redirect, (await s.Get("/library")).Status);
    }

    private static string ResetPath(Session.Page page)
    {
        var a = page.Doc.QuerySelectorAll("a").First(x => x.TextContent.Contains("Open the reset link"));
        return new Uri(a.GetAttribute("href")!).PathAndQuery;
    }

    [Fact]
    public async Task Forgot_and_reset_password_flow()
    {
        var s = _app.NewSession();
        await s.Get("/forgot-password");
        var sent = await s.Post("/forgot-password", new Dictionary<string, string> { ["email"] = "Deniz@Example.com" });
        Assert.Contains("Check your inbox.", sent.Text);
        var link = ResetPath(sent);
        Assert.StartsWith("/reset-password?token=", link);

        var form = await s.Get(link);
        Assert.Contains("New password.", form.Text);
        var token = form.Doc.QuerySelector("input[name=Token]")!.GetAttribute("value")!;

        // validation error keeps the form
        var bad = await s.Post("/reset-password", new Dictionary<string, string> { ["Token"] = token, ["Password"] = "short", ["ConfirmPassword"] = "short" });
        Assert.Contains("Password needs at least 8 characters.", bad.Text);

        var ok = await s.Post("/reset-password", new Dictionary<string, string> { ["Token"] = token, ["Password"] = "brand-new-pass", ["ConfirmPassword"] = "brand-new-pass" });
        Assert.Equal("/", ok.Location);

        // the same link no longer works once the password changed
        Assert.Contains("Link expired.", (await _app.NewSession().Get(link)).Text);
        var reuse = _app.NewSession();
        await reuse.Get("/terms");
        Assert.Contains("Link expired.", (await reuse.Post("/reset-password", new Dictionary<string, string> { ["Token"] = token, ["Password"] = "another-pass1", ["ConfirmPassword"] = "another-pass1" })).Text);

        // new password works, old one does not
        var login = _app.NewSession();
        await login.Login("deniz@example.com", "brand-new-pass");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _app.NewSession().Login("deniz@example.com", Seeder.DemoPassword));
    }

    [Fact]
    public async Task Forgot_password_does_not_reveal_unknown_emails_and_validates()
    {
        var s = _app.NewSession();
        await s.Get("/forgot-password");
        var unknown = await s.Post("/forgot-password", new Dictionary<string, string> { ["email"] = "nobody@example.com" });
        Assert.Contains("Check your inbox.", unknown.Text);
        Assert.DoesNotContain("Open the reset link", unknown.Text);

        var invalid = await s.Post("/forgot-password", new Dictionary<string, string> { ["email"] = "not an email" });
        Assert.Contains("Enter a valid email address.", invalid.Text);
    }

    [Fact]
    public async Task Reset_with_a_garbage_token_shows_expired_page()
    {
        Assert.Contains("Link expired.", (await _app.NewSession().Get("/reset-password?token=garbage")).Text);
        Assert.Contains("Link expired.", (await _app.NewSession().Get("/reset-password")).Text);
    }
}
