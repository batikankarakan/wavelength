using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;
using static Wavelength.Tests.Integration.Admin.AdminHelpers;

namespace Wavelength.Tests.Integration.Admin;

/// <summary>
/// End to end: the activity diagram "Upload and publish a track" (report 4.2) and the state diagram (4.4).
/// Artist uploads → moderator rejects with reason → artist reads it and resubmits → moderator approves → public.
/// </summary>
public class UploadWorkflowTests : IClassFixture<WavelengthFactory>
{
    private readonly WavelengthFactory _app;
    public UploadWorkflowTests(WavelengthFactory app) => _app = app;

    private static readonly string Mp3 = FindMp3();

    private static string FindMp3()
    {
        return Path.Combine(AppContext.BaseDirectory, "test-assets", "neon-highway.mp3");
    }

    private async Task<MultipartFormDataContent> Form(string title, string intent, bool withAudio = true, int duration = 64)
    {
        var genre = await _app.Db(db => db.Genres.Where(g => g.Name == "Electronic").Select(g => g.Id).SingleAsync());
        var form = new MultipartFormDataContent
        {
            { new StringContent(title), "Form.Title" },
            { new StringContent("Line one\nLine two"), "Form.Lyrics" },
            { new StringContent(genre.ToString()), "Form.GenreIds" },
            { new StringContent(duration.ToString()), "Form.DurationSec" },
            { new StringContent(intent), "intent" },
        };
        if (withAudio)
        {
            var audio = new ByteArrayContent(await File.ReadAllBytesAsync(Mp3));
            audio.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            form.Add(audio, "Form.Audio", "my-song.mp3");
        }
        return form;
    }

    [Fact]
    public async Task Full_review_cycle_from_upload_to_public()
    {
        Assert.True(File.Exists(Mp3), Mp3);
        var kora = await _app.LoginAs("kora@wavelength.app");
        var mod = await _app.LoginAs("mod@wavelength.app");
        var modId = await _app.UserId("moderator");
        var adminId = await _app.UserId("admin");
        var pendingNotesBefore = (await _app.Notes(modId, "track_pending")).Count;

        // 1. artist submits the upload form
        var upload = await kora.PostMultipart("/studio/upload", await Form("Integration Anthem", "submit"));
        Assert.Equal(HttpStatusCode.Redirect, upload.Status);
        Assert.Equal("/studio/tracks", upload.Location);
        var track = await _app.Db(db => db.Tracks.AsNoTracking().Include(t => t.TrackGenres).SingleAsync(t => t.Title == "Integration Anthem"));
        Assert.Equal(TrackStatus.Pending, track.Status);
        Assert.Equal(64, track.DurationSec);
        Assert.Single(track.TrackGenres);
        Assert.StartsWith("/uploads/audio/", track.AudioUrl);
        Assert.Equal(HttpStatusCode.OK, (await kora.Client.GetAsync(track.AudioUrl)).StatusCode);

        // 2. moderators and admins are notified; the public can't see it yet
        Assert.Equal(pendingNotesBefore + 1, (await _app.Notes(modId, "track_pending")).Count);
        Assert.Contains(await _app.Notes(adminId, "track_pending"), n => n.Payload.Contains("Integration Anthem"));
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewSession().Get($"/tracks/{track.Id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _app.NewSession().Client.GetAsync($"/api/tracks/{track.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mod.Client.GetAsync($"/api/tracks/{track.Id}")).StatusCode); // moderator can listen
        Assert.Contains("Integration Anthem", (await mod.Get("/admin/review")).Text);

        // 3. moderator rejects with a reason
        await mod.Post($"/admin/review/{track.Id}/reject", F(("reason", "Please trim the silence at the start.")));
        Assert.Equal(TrackStatus.Rejected, (await _app.Track(track.Id)).Status);

        // 4. artist reads the reason on the edit page and in notifications
        var edit = await kora.Get($"/studio/tracks/{track.Id}");
        Assert.Contains("Please trim the silence at the start.", edit.Text);
        Assert.Contains("Please trim the silence", (await kora.Get("/notifications")).Text);

        // 5. artist edits and resubmits
        var genre = await _app.Db(db => db.Genres.Where(g => g.Name == "House").Select(g => g.Id).SingleAsync());
        var resubmit = await kora.PostMultipart($"/studio/tracks/{track.Id}", new MultipartFormDataContent
        {
            { new StringContent("Integration Anthem (Radio Edit)"), "Form.Title" },
            { new StringContent(genre.ToString()), "Form.GenreIds" },
            { new StringContent("64"), "Form.DurationSec" },
            { new StringContent("submit"), "intent" },
        });
        Assert.Equal(HttpStatusCode.Redirect, resubmit.Status);
        var after = await _app.Track(track.Id);
        Assert.Equal(TrackStatus.Pending, after.Status);
        Assert.Equal("Integration Anthem (Radio Edit)", after.Title);

        // 6. moderator approves
        await mod.Post($"/admin/review/{track.Id}/approve");
        Assert.Equal(TrackStatus.Published, (await _app.Track(track.Id)).Status);
        Assert.Contains(await _app.Notes(await _app.UserId("kora"), "track_approved"), n => n.Payload.Contains($"\"track_id\":{track.Id}"));

        // 7. the track is public: details page, search and the player API
        var visitor = _app.NewSession();
        Assert.Contains("Integration Anthem (Radio Edit)", (await visitor.Get($"/tracks/{track.Id}")).Text);
        Assert.Contains("Integration Anthem (Radio Edit)", (await visitor.Get("/search?q=Integration")).Text);
        Assert.Equal(HttpStatusCode.OK, (await visitor.Client.GetAsync($"/api/tracks/{track.Id}")).StatusCode);

        // 8. moderators audit trail contains both decisions
        var audit = await _app.Db(db => db.AuditLogs.Where(a => a.Target.Contains($"#{track.Id} ")).Select(a => a.Action).ToListAsync());
        Assert.Contains("reject_track", audit);
        Assert.Contains("approve_track", audit);
    }

    [Fact]
    public async Task Save_draft_does_not_notify_and_can_be_submitted_later()
    {
        var kora = await _app.LoginAs("kora@wavelength.app");
        var modId = await _app.UserId("moderator");
        var before = (await _app.Notes(modId, "track_pending")).Count;

        await kora.PostMultipart("/studio/upload", await Form("Draft Only", "draft"));
        var id = await _app.TrackId("Draft Only");
        Assert.Equal(TrackStatus.Draft, (await _app.Track(id)).Status);
        Assert.Equal(before, (await _app.Notes(modId, "track_pending")).Count);
        Assert.DoesNotContain("Draft Only", (await (await _app.LoginAs("mod@wavelength.app")).Get("/admin/review")).Text);

        await kora.Post($"/studio/tracks/{id}/submit");
        Assert.Equal(TrackStatus.Pending, (await _app.Track(id)).Status);
        Assert.Equal(before + 1, (await _app.Notes(modId, "track_pending")).Count);
    }

    [Fact]
    public async Task Upload_without_audio_or_duration_is_rejected()
    {
        var kora = await _app.LoginAs("kora@wavelength.app");
        var noAudio = await kora.PostMultipart("/studio/upload", await Form("No Audio Song", "submit", withAudio: false));
        Assert.Equal(HttpStatusCode.OK, noAudio.Status); // form shown again with errors
        Assert.Contains("Choose an audio file", noAudio.Text);

        var noDuration = await kora.PostMultipart("/studio/upload", await Form("No Duration Song", "submit", duration: 0));
        Assert.Contains("could not read the length", noDuration.Text);

        Assert.Equal(0, await _app.Db(db => db.Tracks.CountAsync(t => t.Title.StartsWith("No "))));
    }
}
