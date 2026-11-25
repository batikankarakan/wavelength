using System.Net;
using Wavelength.Tests.Infrastructure;

namespace Wavelength.Tests.Integration;

/// <summary>Every page of the report renders, and table 3.5 (access by role) is enforced.</summary>
public class AccessTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public AccessTests(WavelengthFactory app) => _app = app;

    public static TheoryData<string> PublicPages => new()
    {
        "/", "/about", "/contact", "/register", "/login", "/forgot-password", "/explore", "/search", "/search?q=neon&type=tracks",
        "/charts", "/genres/lo-fi", "/terms", "/users/emre",
    };

    [Theory, MemberData(nameof(PublicPages))]
    public async Task Public_pages_render_for_visitors(string url)
    {
        var page = await _app.NewSession().Get(url);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Wavelength", page.Doc.Title);
    }

    [Fact]
    public async Task Detail_pages_render_for_seeded_content()
    {
        var ids = await _app.Db(db => Task.FromResult(new
        {
            Track = db.Tracks.First(t => t.Title == "Neon Highway").Id,
            Artist = db.ArtistProfiles.First(a => a.StageName == "Kora Vale").Id,
            Album = db.Albums.First(a => a.Title == "Night Transit").Id,
            Playlist = db.Playlists.First(p => p.Title == "Late Night Coding").Id,
        }));
        var s = _app.NewSession();
        Assert.Contains("Neon Highway", (await s.Get($"/tracks/{ids.Track}")).Text);
        Assert.Contains("Kora Vale", (await s.Get($"/artists/{ids.Artist}")).Text);
        Assert.Contains("Night Transit", (await s.Get($"/albums/{ids.Album}")).Text);
        Assert.Contains("Late Night Coding", (await s.Get($"/playlists/{ids.Playlist}")).Text);
    }

    [Theory]
    [InlineData("/tracks/999999")]
    [InlineData("/artists/999999")]
    [InlineData("/albums/999999")]
    [InlineData("/playlists/999999")]
    [InlineData("/users/nobody-here")]
    [InlineData("/genres/not-a-genre")]
    [InlineData("/s/badtoken")]
    [InlineData("/definitely/not/a/page")]
    public async Task Missing_content_gives_the_404_page(string url)
    {
        var page = await _app.NewSession().Get(url);
        Assert.Equal(HttpStatusCode.NotFound, page.Status);
        Assert.Contains("Track not found", page.Text);
    }

    public static TheoryData<string> ListenerPages => new() { "/library", "/library/playlists", "/playlists/new", "/history", "/following", "/notifications", "/settings" };
    public static TheoryData<string> StudioPages => new() { "/studio", "/studio/upload", "/studio/tracks", "/studio/albums", "/studio/profile" };
    public static TheoryData<string> StaffPages => new() { "/admin", "/admin/review", "/admin/reports" };
    public static TheoryData<string> AdminPages => new() { "/admin/users", "/admin/genres", "/admin/messages", "/admin/audit" };

    [Theory]
    [MemberData(nameof(ListenerPages))] [MemberData(nameof(StudioPages))] [MemberData(nameof(StaffPages))] [MemberData(nameof(AdminPages))]
    public async Task Visitors_are_sent_to_login(string url)
    {
        var page = await _app.NewSession().Get(url);
        Assert.Equal(HttpStatusCode.Redirect, page.Status);
        Assert.StartsWith("/login?ReturnUrl=", page.Location);
    }

    [Theory, MemberData(nameof(ListenerPages))]
    public async Task Listener_pages_open_for_listeners(string url) =>
        Assert.Equal(HttpStatusCode.OK, (await (await _app.LoginAs("listener@wavelength.app")).Get(url)).Status);

    [Theory, MemberData(nameof(StudioPages))]
    public async Task Studio_is_for_artists_only(string url)
    {
        Assert.Equal(HttpStatusCode.OK, (await (await _app.LoginAs("kora@wavelength.app")).Get(url)).Status);
        var denied = await (await _app.LoginAs("listener@wavelength.app")).Get(url);
        Assert.Equal(HttpStatusCode.Redirect, denied.Status);
        Assert.Contains("/403", denied.Location);
    }

    [Theory, MemberData(nameof(StaffPages))]
    public async Task Moderation_is_for_moderators_and_admins(string url)
    {
        Assert.Equal(HttpStatusCode.OK, (await (await _app.LoginAs("mod@wavelength.app")).Get(url)).Status);
        Assert.Equal(HttpStatusCode.OK, (await (await _app.LoginAs("admin@wavelength.app")).Get(url)).Status);
        Assert.Contains("/403", (await (await _app.LoginAs("kora@wavelength.app")).Get(url)).Location);
    }

    [Theory, MemberData(nameof(AdminPages))]
    public async Task Administration_is_for_admins_only(string url)
    {
        Assert.Equal(HttpStatusCode.OK, (await (await _app.LoginAs("admin@wavelength.app")).Get(url)).Status);
        Assert.Contains("/403", (await (await _app.LoginAs("mod@wavelength.app")).Get(url)).Location);
    }

    [Fact]
    public async Task Forbidden_page_renders()
    {
        var page = await _app.NewSession().Get("/403");
        Assert.Equal(HttpStatusCode.Forbidden, page.Status);
        Assert.Contains("Backstage only", page.Text);
    }

    [Fact]
    public async Task Posts_without_an_antiforgery_token_are_rejected()
    {
        var s = await _app.LoginAs("listener@wavelength.app");
        var res = await s.Post("/history/clear", Array.Empty<KeyValuePair<string, string>>(), withToken: false);
        Assert.Equal(HttpStatusCode.BadRequest, res.Status);
    }

    [Fact]
    public async Task Nav_shows_role_specific_links()
    {
        Assert.DoesNotContain("href=\"/studio\"", (await (await _app.LoginAs("listener@wavelength.app")).Get("/")).Html);
        Assert.Contains("href=\"/studio\"", (await (await _app.LoginAs("kora@wavelength.app")).Get("/")).Html);
        Assert.Contains("href=\"/admin\"", (await (await _app.LoginAs("mod@wavelength.app")).Get("/")).Html);
    }
}
