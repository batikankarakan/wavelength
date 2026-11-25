using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Integration.Public;

/// <summary>Home, explore, search, genre, charts, artist and user pages show the right data.</summary>
public class DiscoverTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public DiscoverTests(WavelengthFactory app) => _app = app;

    [Fact]
    public async Task Home_shows_the_most_played_published_tracks()
    {
        var top = await _app.Db(db => db.Tracks.Where(t => t.Status == TrackStatus.Published).OrderByDescending(t => t.PlaysCount).Take(8).Select(t => t.Title).ToListAsync());
        var html = (await _app.NewSession().Get("/")).Html;
        foreach (var title in top.Where(t => Regex.IsMatch(t, "^[A-Za-z0-9 ]+$")))
            Assert.Contains(title, html);
        Assert.DoesNotContain("After Hours Club", html); // pending
        Assert.Contains("Share.", html);
    }

    [Fact]
    public async Task About_and_terms_have_their_content()
    {
        var about = await _app.NewSession().Get("/about");
        var decoded = WebUtility.HtmlDecode(about.Html);
        Assert.Contains("Emre Men", decoded);
        Assert.Contains("Batıkan Karakan", decoded);
        Assert.Equal(HttpStatusCode.OK, (await _app.NewSession().Get("/terms")).Status);
    }

    [Fact]
    public async Task Search_finds_tracks_artists_albums_and_playlists()
    {
        var s = _app.NewSession();
        var all = await s.Get("/search?q=neon");
        Assert.Contains("Neon Highway", all.Text);
        Assert.Contains("Chrome & Neon", all.Text);

        var artists = await s.Get("/search?q=kora&type=artists");
        Assert.Contains("Kora Vale", artists.Text);

        var albums = await s.Get("/search?q=transit&type=albums");
        Assert.Contains("Night Transit", albums.Text);

        var lyrics = await s.Get("/search?q=overpass&type=tracks");
        Assert.Contains("Neon Highway", lyrics.Text); // lyrics are searchable

        var hidden = await s.Get("/search?q=Moth&type=tracks");
        Assert.DoesNotContain("Moth to the Flame", hidden.Text); // pending track
    }

    [Theory]
    [InlineData("popular")]
    [InlineData("new")]
    [InlineData("az")]
    [InlineData("weird")]
    public async Task Search_genre_filter_and_sorts(string sort)
    {
        var page = await _app.NewSession().Get($"/search?type=tracks&genre=lo-fi&sort={sort}");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Slow Burn Rain", page.Text);
        Assert.Contains("Static Bloom", page.Text);
        Assert.DoesNotContain("Rust & Thunder", page.Text);
        Assert.DoesNotContain("Concrete Crown", page.Text);
    }

    [Theory]
    [InlineData("artists")]
    [InlineData("albums")]
    [InlineData("playlists")]
    public async Task Search_other_types_with_genre_and_sort(string type)
    {
        foreach (var sort in new[] { "popular", "new", "az" })
            Assert.Equal(HttpStatusCode.OK, (await _app.NewSession().Get($"/search?type={type}&genre=synthwave&sort={sort}&q=a")).Status);
    }

    [Fact]
    public async Task Genre_page_lists_only_that_genre_and_paginates()
    {
        var page = await _app.NewSession().Get("/genres/rock");
        Assert.Contains("Rust & Thunder", page.Text);
        Assert.Contains("Iron Lullaby", page.Text);
        Assert.DoesNotContain("Coffee Cloud", page.Text);
        Assert.Equal(HttpStatusCode.OK, (await _app.NewSession().Get("/genres/rock?sort=new&page=99")).Status);
    }

    [Fact]
    public async Task Charts_rank_tracks_by_plays_in_the_last_7_days()
    {
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var top = await _app.Db(db => db.ListeningHistory.Where(h => h.PlayedAt > weekAgo && h.Track.Status == TrackStatus.Published)
            .GroupBy(h => h.Track.Title).Select(g => new { Title = g.Key, Plays = g.Count() }).OrderByDescending(x => x.Plays).FirstAsync());
        var page = await _app.NewSession().Get("/charts");
        Assert.Contains(top.Title, page.Text);

        // a burst of plays this week pushes a track to number one
        Assert.DoesNotContain("Kintsugi", page.Text); // rejected tracks never chart
        var honey = await _app.Db(db => db.Tracks.Where(t => t.Title == "Honey Tape").Select(t => t.Id).SingleAsync());
        var users = await _app.Db(db => db.Users.Select(u => u.Id).Take(5).ToListAsync());
        await _app.Db(async db =>
        {
            for (var i = 0; i < 400; i++) db.ListeningHistory.Add(new ListeningHistory { UserId = users[i % users.Count], TrackId = honey, SecondsListened = 40, PlayedAt = DateTime.UtcNow.AddHours(-1) });
            await db.SaveChangesAsync();
        });
        var after = await _app.NewSession().Get("/charts");
        var firstRow = after.Text.IndexOf("Honey Tape", StringComparison.Ordinal);
        Assert.True(firstRow >= 0);
        if (top.Title != "Honey Tape")
            Assert.True(firstRow < after.Text.IndexOf(top.Title, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Explore_shows_genres_releases_and_recommendations()
    {
        var anon = await _app.NewSession().Get("/explore");
        Assert.Contains("Lo-fi", anon.Text);
        Assert.Contains("Synthwave", anon.Text);
        var signedIn = await (await _app.LoginAs("listener@wavelength.app")).Get("/explore");
        Assert.Equal(HttpStatusCode.OK, signedIn.Status);
        Assert.DoesNotContain("After Hours Club", signedIn.Text);
    }

    [Fact]
    public async Task Artist_page_shows_profile_stats_and_follow_state()
    {
        var kora = await _app.Db(db => db.ArtistProfiles.Where(a => a.StageName == "Kora Vale").Select(a => new { a.Id, a.UserId }).SingleAsync());
        var page = await _app.NewSession().Get($"/artists/{kora.Id}");
        Assert.Contains("Kora Vale", page.Text);
        Assert.Contains("Norway", page.Text);
        Assert.Contains("Neon Highway", page.Text);
        Assert.Contains("Night Transit", page.Text);
        Assert.DoesNotContain("After Hours Club", page.Text);

        var s = await _app.LoginAs("leo@example.com");
        Assert.Contains($"/follow/{kora.UserId}", (await s.Get($"/artists/{kora.Id}")).Html);

        await _app.Db(db => db.Users.Where(u => u.Id == kora.UserId).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false)));
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewSession().Get($"/artists/{kora.Id}")).Status);
        await _app.Db(db => db.Users.Where(u => u.Id == kora.UserId).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, true)));
    }

    [Fact]
    public async Task Album_page_lists_published_tracks()
    {
        var id = await _app.Db(db => db.Albums.Where(a => a.Title == "Rainy Window Tapes").Select(a => a.Id).SingleAsync());
        var page = await _app.NewSession().Get($"/albums/{id}");
        Assert.Contains("Slow Burn Rain", page.Text);
        Assert.Contains("Coffee Cloud", page.Text);
        Assert.Contains("Static Bloom", page.Text);
    }

    [Fact]
    public async Task User_profile_shows_bio_and_public_playlists()
    {
        var page = await _app.NewSession().Get("/users/emre");
        Assert.Contains("Emre Men", page.Text);
        Assert.Contains("Group Project Panic", page.Text);
        Assert.Equal(HttpStatusCode.OK, (await (await _app.LoginAs("emre@wavelength.app")).Get("/users/emre")).Status);
    }
}
