using System.Net;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Tests.Integration.Listener;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Integration.Studio;

/// <summary>My tracks, submit, edit and delete: the track state diagram (report figure 6) from the artist side.</summary>
[Collection("T2 uploads")]
public class TrackLifecycleTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public TrackLifecycleTests(WavelengthFactory app) => _app = app;

    private Task<Track> TrackBy(string title) => _app.Db(db => db.Tracks.AsNoTracking().Include(t => t.TrackGenres).FirstAsync(t => t.Title == title));
    private Task<string> Status(int id) => _app.Db(db => db.Tracks.Where(t => t.Id == id).Select(t => t.Status).FirstAsync());
    private Task<int> PendingNotes(int trackId) => _app.Db(db => db.Notifications.CountAsync(n => n.Type == "track_pending" && n.Payload.Contains($"\"track_id\":{trackId}}}")));
    private Task<int> GenreId(string slug) => _app.Db(db => db.Genres.Where(g => g.Slug == slug).Select(g => g.Id).FirstAsync());

    private static Dictionary<string, string> EditFields(string title, int genre, string intent = "save") => new()
    {
        ["Form.Title"] = title, ["Form.GenreIds"] = genre.ToString(), ["intent"] = intent, ["Form.Lyrics"] = "",
    };

    [Fact]
    public async Task My_tracks_filters_by_status()
    {
        var s = await _app.LoginAs("kora@wavelength.app");
        var all = await s.Get("/studio/tracks");
        Assert.Contains("Neon Highway", all.Text);
        Assert.Contains("After Hours Club", all.Text);

        var pending = await s.Get("/studio/tracks?status=pending");
        Assert.Contains("After Hours Club", pending.Text);
        Assert.DoesNotContain("Warehouse Sunrise", pending.Text);

        var bogus = await s.Get("/studio/tracks?status=bogus");
        Assert.Contains("Neon Highway", bogus.Text);
        Assert.DoesNotContain("Rust & Thunder", bogus.Text); // other artists' tracks never show
    }

    [Fact]
    public async Task Submitting_a_draft_sends_it_to_review()
    {
        var draft = await TrackBy("Untitled Demo 04");
        var s = await _app.LoginAs("djatlas@wavelength.app");
        var res = await s.Post($"/studio/tracks/{draft.Id}/submit", new { returnUrl = "/studio" });
        Assert.Equal("/studio", res.Location);
        Assert.Equal(TrackStatus.Pending, await Status(draft.Id));
        Assert.Equal(2, await PendingNotes(draft.Id)); // moderator + admin

        // a pending/published track can't be submitted again
        await s.Post($"/studio/tracks/{draft.Id}/submit", new { returnUrl = "https://evil.example.com" });
        Assert.Equal(2, await PendingNotes(draft.Id));
        Assert.Contains("Only drafts and rejected tracks", (await s.Get("/studio/tracks")).Text);
    }

    [Fact]
    public async Task Rejected_track_shows_the_reason_then_goes_back_to_draft_and_to_review()
    {
        var t = await TrackBy("Kintsugi");
        var genre = t.TrackGenres.First().GenreId;
        var s = await _app.LoginAs("selin@wavelength.app");

        var page = await s.Get($"/studio/tracks/{t.Id}");
        var text = System.Text.RegularExpressions.Regex.Replace(page.Text, @"\s+", " ");
        Assert.True(text.Contains("The audio clips at the end"), $"status {page.Status}, track status {t.Status}, page: " + text[..Math.Min(text.Length, 1500)]);

        var save = await s.Post($"/studio/tracks/{t.Id}", EditFields("Kintsugi (remaster)", genre));
        Assert.Equal($"/studio/tracks/{t.Id}", save.Location);
        Assert.Equal(TrackStatus.Draft, await Status(t.Id));
        Assert.Equal(0, await PendingNotes(t.Id));

        await s.Post($"/studio/tracks/{t.Id}", EditFields("Kintsugi (remaster)", genre, "submit"));
        Assert.Equal(TrackStatus.Pending, await Status(t.Id));
        Assert.Equal(2, await PendingNotes(t.Id));
    }

    [Fact]
    public async Task Resubmit_from_my_tracks_works_for_rejected_tracks()
    {
        int id = 0;
        await _app.Db(async db =>
        {
            var selin = await db.ArtistProfiles.FirstAsync(a => a.StageName == "Selin Ay");
            var t = new Track { ArtistId = selin.Id, Title = "Second Try", AudioUrl = "/media/audio/kintsugi.mp3", DurationSec = 58, Status = TrackStatus.Rejected };
            db.Tracks.Add(t);
            await db.SaveChangesAsync();
            id = t.Id;
        });
        var s = await _app.LoginAs("selin@wavelength.app");
        Assert.Equal(HttpStatusCode.OK, (await s.Get($"/studio/tracks/{id}")).Status); // no rejection notification: no reason box
        await s.Post($"/studio/tracks/{id}/submit", new { });
        Assert.Equal(TrackStatus.Pending, await Status(id));
    }

    [Fact]
    public async Task Published_metadata_edit_stays_published_but_new_audio_goes_back_to_review()
    {
        var t = await TrackBy("Neon Highway");
        var house = await GenreId("house");
        var s = await _app.LoginAs("kora@wavelength.app");

        var res = await s.Post($"/studio/tracks/{t.Id}", new Dictionary<string, string>
        { ["Form.Title"] = "Neon Highway (Extended)", ["Form.GenreIds"] = house.ToString(), ["intent"] = "save", ["Form.Lyrics"] = "new words" });
        Assert.Equal(HttpStatusCode.Redirect, res.Status);
        var after = await TrackBy("Neon Highway (Extended)");
        Assert.Equal(TrackStatus.Published, after.Status);
        Assert.Equal(new[] { house }, after.TrackGenres.Select(g => g.GenreId));
        Assert.Equal("new words", after.Lyrics);

        var form = T2Files.Form(("Form.Title", "Neon Highway (Extended)"), ("Form.GenreIds", house.ToString()), ("intent", "save"), ("Form.DurationSec", "70"))
            .File("Form.Audio", "v2.mp3", "audio/mpeg", T2Files.Mp3)
            .File("Form.Cover", "v2.png", "image/png", T2Files.Png);
        await s.PostMultipart($"/studio/tracks/{t.Id}", form);
        var v2 = await TrackBy("Neon Highway (Extended)");
        Assert.Equal(TrackStatus.Pending, v2.Status);
        Assert.Equal(70, v2.DurationSec);
        Assert.StartsWith("/uploads/audio/", v2.AudioUrl);
        Assert.StartsWith("/uploads/covers/", v2.CoverUrl);
        Assert.Equal(2, await PendingNotes(t.Id));
        Assert.True(await _app.FileExists("/media/audio/neon-highway.mp3")); // seeded media is never deleted

        // removing the cover
        await s.Post($"/studio/tracks/{t.Id}", new Dictionary<string, string>
        { ["Form.Title"] = "Neon Highway (Extended)", ["Form.GenreIds"] = house.ToString(), ["intent"] = "save", ["Form.RemoveCover"] = "true" });
        Assert.Null((await TrackBy("Neon Highway (Extended)")).CoverUrl);
    }

    [Fact]
    public async Task Edit_validation_keeps_the_track_unchanged()
    {
        var t = await TrackBy("Warehouse Sunrise");
        var s = await _app.LoginAs("kora@wavelength.app");
        var res = await s.Post($"/studio/tracks/{t.Id}", new Dictionary<string, string> { ["Form.Title"] = "Renamed", ["intent"] = "save" });
        Assert.Equal(HttpStatusCode.OK, res.Status);
        Assert.Contains("Pick at least one genre.", res.Text);
        Assert.Equal("Warehouse Sunrise", (await TrackBy("Warehouse Sunrise")).Title);

        var noLength = await s.PostMultipart($"/studio/tracks/{t.Id}",
            T2Files.Form(("Form.Title", "Renamed"), ("Form.GenreIds", t.TrackGenres.First().GenreId.ToString()), ("intent", "save"), ("Form.DurationSec", "0"))
                .File("Form.Audio", "x.mp3", "audio/mpeg", T2Files.Mp3));
        Assert.Contains("We could not read the length", noLength.Text);

        var badAudio = await s.PostMultipart($"/studio/tracks/{t.Id}",
            T2Files.Form(("Form.Title", "Renamed"), ("Form.GenreIds", t.TrackGenres.First().GenreId.ToString()), ("intent", "save"), ("Form.DurationSec", "60"))
                .File("Form.Audio", "x.exe", "audio/mpeg", new byte[] { 1 }));
        Assert.Contains("Audio must be MP3, WAV, OGG or M4A.", badAudio.Text);
    }

    [Fact]
    public async Task Pending_and_hidden_tracks_keep_their_status_on_edit()
    {
        var t = await TrackBy("Moth to the Flame");
        var s = await _app.LoginAs("hollowkeys@wavelength.app");
        await s.Post($"/studio/tracks/{t.Id}", EditFields("Moth to the Flame", t.TrackGenres.First().GenreId, "submit"));
        Assert.Equal(TrackStatus.Pending, await Status(t.Id));
        Assert.Equal(2, await PendingNotes(t.Id)); // no extra notification (seeded ones only)
    }

    [Fact]
    public async Task Other_artists_tracks_are_not_found()
    {
        var t = await TrackBy("Rust & Thunder");
        var s = await _app.LoginAs("kora@wavelength.app");
        Assert.Equal(HttpStatusCode.NotFound, (await s.Get($"/studio/tracks/{t.Id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/studio/tracks/{t.Id}", EditFields("Mine now", t.TrackGenres.First().GenreId))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/studio/tracks/{t.Id}/submit", new { })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Post($"/studio/tracks/{t.Id}/delete", new { })).Status);
        Assert.Equal("Rust & Thunder", (await TrackBy("Rust & Thunder")).Title);
    }

    [Fact]
    public async Task Delete_rules_follow_the_status()
    {
        var s = await _app.LoginAs("kora@wavelength.app");
        var house = await GenreId("house");

        // draft uploaded by the artist: deleted together with its file
        await s.PostMultipart("/studio/upload", T2Files.Form(("Form.Title", "Throwaway"), ("Form.GenreIds", house.ToString()), ("intent", "draft"), ("Form.DurationSec", "60"))
            .File("Form.Audio", "t.mp3", "audio/mpeg", T2Files.Mp3));
        var draft = await TrackBy("Throwaway");
        var file = draft.AudioUrl;
        Assert.True(await _app.FileExists(file));
        Assert.Equal("/studio/tracks", (await s.Post($"/studio/tracks/{draft.Id}/delete", new { })).Location);
        Assert.False(await _app.Db(db => db.Tracks.AnyAsync(t => t.Id == draft.Id)));
        Assert.False(await _app.FileExists(file));

        // pending: refused
        var pending = await TrackBy("After Hours Club");
        await s.Post($"/studio/tracks/{pending.Id}/delete", new { });
        Assert.Equal(TrackStatus.Pending, await Status(pending.Id));
        Assert.Contains("being reviewed", (await s.Get("/studio/tracks")).Text);

        // hidden: refused
        var northern = await TrackBy("Northern Drift");
        await _app.Db(db => db.Tracks.Where(t => t.Id == northern.Id).ExecuteUpdateAsync(x => x.SetProperty(t => t.Status, TrackStatus.Hidden)));
        await s.Post($"/studio/tracks/{northern.Id}/delete", new { });
        Assert.True(await _app.Db(db => db.Tracks.AnyAsync(t => t.Id == northern.Id)));
        Assert.Contains("under moderation", (await s.Get("/studio/tracks")).Text);

        // published: deleted with likes, history and playlist entries; seeded media file stays
        var neon = await TrackBy("Velvet Circuit");
        var nova = await _app.LoginAs("novadrift@wavelength.app");
        await nova.Post($"/studio/tracks/{neon.Id}/delete", new { });
        await _app.Db(async db =>
        {
            Assert.False(await db.Tracks.AnyAsync(t => t.Id == neon.Id));
            Assert.False(await db.TrackLikes.AnyAsync(l => l.TrackId == neon.Id));
            Assert.False(await db.ListeningHistory.AnyAsync(h => h.TrackId == neon.Id));
            Assert.False(await db.PlaylistTracks.AnyAsync(p => p.TrackId == neon.Id));
        });
        Assert.True(await _app.FileExists(neon.AudioUrl));

        Assert.Equal(HttpStatusCode.NotFound, (await s.Post("/studio/tracks/999999/delete", new { })).Status);
    }
}
