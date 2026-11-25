using System;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Models;
using Wavelength.Web.Services;

namespace Wavelength.Web.Controllers
{
    /// <summary>JSON endpoints used by the player and the AJAX widgets (sequence diagram 4.3 in the report).</summary>
    [RoutePrefix("api")]
    public class ApiController : BaseController
    {
        // 2: GET /api/tracks/{id} -> 3: SELECT track (published only) -> 5: 200 OK or 5b: 404
        [HttpGet, Route("tracks/{id:int}")]
        public ActionResult GetTrack(int id)
        {
            var uid = CurrentUserId;
            var staff = User.IsStaff();
            var t = Db.Tracks.AsNoTracking()
                .Where(x => x.Id == id && (x.Status == TrackStatus.Published || staff || x.Artist.UserId == uid))
                .Select(x => new
                {
                    x.Id, x.Title, x.AudioUrl, x.CoverUrl, x.DurationSec, x.ArtistId, Artist = x.Artist.StageName,
                    Color = x.TrackGenres.OrderBy(g => g.GenreId).Select(g => g.Genre.Color).FirstOrDefault(),
                }).FirstOrDefault();
            if (t == null) return CamelJson(new { error = "Track unavailable" }, 404);
            var color = t.Color ?? Fmt.ColorFor(t.Id);
            return CamelJson(new { t.Id, t.Title, t.AudioUrl, t.CoverUrl, t.DurationSec, t.ArtistId, t.Artist, Color = color, OnColor = Fmt.On(color) });
        }

        public class PlayRequest
        {
            public int TrackId { get; set; }
            public int Seconds { get; set; }
        }

        // 7: POST /api/plays -> 8: INSERT INTO listening_history -> 9: UPDATE tracks.plays_count + 1 -> 10: 201 Created
        [HttpPost, Route("plays")]
        public ActionResult Play(PlayRequest req)
        {
            var track = req == null ? null : Db.Tracks.FirstOrDefault(t => t.Id == req.TrackId && t.Status == TrackStatus.Published);
            if (track == null) return CamelJson(new { error = "Track unavailable" }, 404);
            var seconds = Math.Max(0, Math.Min(req.Seconds, Math.Max(track.DurationSec, 1)));

            var uid = CurrentUserId;
            if (uid != null) // visitors are counted but have no history row (listening_history.user_id is required)
                Db.ListeningHistory.Add(new ListeningHistory { UserId = uid.Value, TrackId = track.Id, SecondsListened = seconds });
            Db.SaveChanges();
            Db.Database.ExecuteSqlCommand("UPDATE tracks SET plays_count = plays_count + 1 WHERE id = @p0", track.Id);
            return CamelJson(new { ok = true }, 201);
        }

        [HttpPost, Route("tracks/{id:int}/like")]
        public ActionResult ToggleLike(int id)
        {
            var uid = CurrentUserId;
            if (uid == null) return CamelJson(new { error = "Log in first" }, 401);
            if (!Db.Tracks.Any(t => t.Id == id && t.Status == TrackStatus.Published)) return CamelJson(new { error = "Track unavailable" }, 404);
            var like = Db.TrackLikes.Find(uid.Value, id);
            if (like == null) Db.TrackLikes.Add(new TrackLike { UserId = uid.Value, TrackId = id });
            else Db.TrackLikes.Remove(like);
            Db.SaveChanges();
            return CamelJson(new { liked = like == null, likes = Db.TrackLikes.Count(l => l.TrackId == id) });
        }

        /// <summary>Published track search for the playlist editor.</summary>
        [HttpGet, Route("tracks/search")]
        public ActionResult Search(string q)
        {
            q = (q ?? "").Trim();
            var items = Db.Tracks.AsNoTracking().Published()
                .Where(t => q == "" || t.Title.Contains(q) || t.Artist.StageName.Contains(q))
                .OrderByDescending(t => t.PlaysCount).Take(12).ToItems().ToList();
            return CamelJson(items.Select(i => new { i.Id, i.Title, i.Artist, i.CoverUrl, i.Color, i.DurationSec }));
        }

        [HttpGet, Route("notifications/unread")]
        public ActionResult Unread()
        {
            var uid = CurrentUserId;
            if (uid == null) return CamelJson(new { error = "Log in first" }, 401);
            return CamelJson(new { count = Db.Notifications.Count(n => n.UserId == uid && !n.IsRead) });
        }
    }
}
