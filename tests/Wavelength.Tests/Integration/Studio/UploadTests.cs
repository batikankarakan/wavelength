using System.Net;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Tests.Integration.Listener;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Integration.Studio;

/// <summary>Upload track (activity diagram, report figure 4): draft or submit for review, with validation.</summary>
[Collection("T2 uploads")]
public class UploadTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public UploadTests(WavelengthFactory app) => _app = app;

    private Task<int> GenreId(string slug) => _app.Db(db => db.Genres.Where(g => g.Slug == slug).Select(g => g.Id).FirstAsync());
    private Task<int> AlbumId(string title) => _app.Db(db => db.Albums.Where(a => a.Title == title).Select(a => a.Id).FirstAsync());

    private static MultipartFormDataContent Upload(string title, string intent, IEnumerable<int> genres, int duration = 64,
        bool audio = true, int? albumId = null, string audioName = "song.mp3", string audioType = "audio/mpeg")
    {
        var c = T2Files.Form(("Form.Title", title), ("intent", intent), ("Form.DurationSec", duration.ToString()), ("Form.Lyrics", "la la la"));
        foreach (var g in genres) c.Add(new StringContent(g.ToString()), "Form.GenreIds");
        if (albumId != null) c.Add(new StringContent(albumId.ToString()!), "Form.AlbumId");
        if (audio) c.File("Form.Audio", audioName, audioType, audioName.EndsWith(".mp3") ? T2Files.Mp3 : new byte[] { 1, 2, 3, 4 });
        return c;
    }

    [Fact]
    public async Task Upload_page_lists_genres_and_own_albums()
    {
        var s = await _app.LoginAs("kora@wavelength.app");
        var page = await s.Get("/studio/upload");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Synthwave", page.Text);
        Assert.Contains("Night Transit", page.Html);
        Assert.DoesNotContain("Blue Hour", page.Html); // selin's album
    }

    [Fact]
    public async Task Save_draft_stores_the_file_and_does_not_notify_staff()
    {
        var notesBefore = await _app.Db(db => db.Notifications.CountAsync(n => n.Type == "track_pending"));
        var s = await _app.LoginAs("kora@wavelength.app");

        var res = await s.PostMultipart("/studio/upload", Upload("  Draft Song  ", "draft", new[] { await GenreId("house") }));
        Assert.Equal("/studio/tracks", res.Location);

        var t = await _app.Db(db => db.Tracks.Include(x => x.TrackGenres).FirstAsync(x => x.Title == "Draft Song"));
        Assert.Equal(TrackStatus.Draft, t.Status);
        Assert.Equal(64, t.DurationSec);
        Assert.Equal("la la la", t.Lyrics);
        Assert.Single(t.TrackGenres);
        Assert.StartsWith("/uploads/audio/", t.AudioUrl);
        Assert.True(await _app.FileExists(t.AudioUrl));
        Assert.Equal(notesBefore, await _app.Db(db => db.Notifications.CountAsync(n => n.Type == "track_pending")));
        Assert.Contains("saved as a draft", (await s.Get("/studio/tracks")).Text);
    }

    [Fact]
    public async Task Submit_for_review_makes_it_pending_and_notifies_every_active_staff_member()
    {
        // a banned moderator must not be notified
        await _app.Db(async db =>
        {
            var u = new User { Username = "oldmod", Email = "oldmod@example.com", DisplayName = "Old Mod", Role = Roles.Moderator, IsActive = false, PasswordHash = "x" };
            db.Users.Add(u);
            await db.SaveChangesAsync();
        });
        var staff = await _app.Db(db => db.Users.Where(u => u.IsActive && (u.Role == Roles.Moderator || u.Role == Roles.Admin)).Select(u => u.Id).ToListAsync());
        var s = await _app.LoginAs("kora@wavelength.app");
        var album = await AlbumId("Night Transit");

        var res = await s.PostMultipart("/studio/upload",
            Upload("Review Me", "submit", new[] { await GenreId("electronic"), await GenreId("house") }, duration: 99999, albumId: album)
                .File("Form.Cover", "cover.png", "image/png", T2Files.Png));
        Assert.Equal("/studio/tracks", res.Location);

        var t = await _app.Db(db => db.Tracks.FirstAsync(x => x.Title == "Review Me"));
        Assert.Equal(TrackStatus.Pending, t.Status);
        Assert.Equal(3600, t.DurationSec); // clamped
        Assert.Equal(album, t.AlbumId);
        Assert.StartsWith("/uploads/covers/", t.CoverUrl);

        var notified = await _app.Db(db => db.Notifications.Where(n => n.Type == "track_pending" && n.Payload.Contains($"\"track_id\":{t.Id}}}")).Select(n => n.UserId).ToListAsync());
        Assert.Equal(staff.OrderBy(x => x), notified.OrderBy(x => x));
        Assert.Contains("\"artist\":\"Kora Vale\"", await _app.Db(db => db.Notifications.Where(n => n.Type == "track_pending" && n.Payload.Contains($"\"track_id\":{t.Id}}}")).Select(n => n.Payload).FirstAsync()));
    }

    public static TheoryData<string, string> Invalid => new()
    {
        { "no-audio", "Choose an audio file." },
        { "wrong-type", "Audio must be MP3, WAV, OGG or M4A." },
        { "no-genres", "Pick at least one genre." },
        { "four-genres", "Pick at most 3 genres." },
        { "unknown-genre", "Unknown genre." },
        { "no-duration", "We could not read the length" },
        { "other-album", "Choose one of your albums." },
        { "no-title", "Give your track a title." },
        { "bad-cover", "Images must be JPG, PNG, WEBP or GIF." },
    };

    [Theory, MemberData(nameof(Invalid))]
    public async Task Invalid_uploads_are_refused(string kind, string error)
    {
        var genres = await _app.Db(db => db.Genres.OrderBy(g => g.Id).Select(g => g.Id).ToListAsync());
        var one = new[] { genres[0] };
        var title = $"Invalid {kind}";
        var form = kind switch
        {
            "no-audio" => Upload(title, "submit", one, audio: false),
            "wrong-type" => Upload(title, "submit", one, audioName: "song.txt", audioType: "text/plain"),
            "no-genres" => Upload(title, "submit", Array.Empty<int>()),
            "four-genres" => Upload(title, "submit", genres.Take(4)),
            "unknown-genre" => Upload(title, "submit", new[] { 999999 }),
            "no-duration" => Upload(title, "submit", one, duration: 0),
            "other-album" => Upload(title, "submit", one, albumId: await AlbumId("Blue Hour")),
            "no-title" => Upload("", "submit", one),
            "bad-cover" => Upload(title, "submit", one).File("Form.Cover", "c.svg", "image/svg+xml", new byte[] { 60 }),
            _ => throw new ArgumentException(kind),
        };
        var count = await _app.Db(db => db.Tracks.CountAsync());
        var s = await _app.LoginAs("kora@wavelength.app");

        var res = await s.PostMultipart("/studio/upload", form);
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains(error, res.Text);
        Assert.Equal(count, await _app.Db(db => db.Tracks.CountAsync()));
    }

    [Fact]
    public async Task Artist_account_without_profile_gets_one_on_first_studio_visit()
    {
        await _app.Db(async db =>
        {
            var leo = await db.Users.FirstAsync(u => u.Username == "leo");
            leo.Role = Roles.Artist;
            await db.SaveChangesAsync();
        });
        var s = await _app.LoginAs("leo@example.com");
        Assert.Equal(HttpStatusCode.OK, (await s.Get("/studio")).Status);
        var profile = await _app.Db(db => db.ArtistProfiles.FirstOrDefaultAsync(a => a.User.Username == "leo"));
        Assert.NotNull(profile);
        Assert.Equal("Leo Brandt", profile!.StageName);
    }
}
