using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Models;
using Wavelength.Web.Services;

namespace Wavelength.Web.Controllers
{
    /// <summary>Public page 9: Track details (player, lyrics, genres, comments, like, share, add to playlist, report).</summary>
    public class TracksController : BaseController
    {
        [HttpGet, Route("tracks/{id:int}")]
        public ActionResult Details(int id)
        {
            var uid = CurrentUserId;
            var staff = User.IsStaff();
            var row = Db.Tracks.AsNoTracking().Where(t => t.Id == id)
                .Select(t => new { t.Status, t.Lyrics, ArtistUserId = t.Artist.UserId, t.Artist.User.AvatarUrl, Active = t.Artist.User.IsActive }).FirstOrDefault();
            var isOwner = row != null && row.ArtistUserId == uid;
            // Only published tracks are public; the artist and moderators can preview other states.
            if (row == null || (row.Status != TrackStatus.Published && !isOwner && !staff) || (!row.Active && !staff)) return NotFoundView();

            var track = Db.Tracks.AsNoTracking().Where(t => t.Id == id).ToItems().First();
            var vm = new TrackPageVm
            {
                Track = track, Lyrics = row.Lyrics, ArtistUserId = row.ArtistUserId, ArtistAvatar = row.AvatarUrl, IsOwner = isOwner,
                Genres = Db.TrackGenres.Where(g => g.TrackId == id).Select(g => new GenreItem { Id = g.GenreId, Name = g.Genre.Name, Slug = g.Genre.Slug, Color = g.Genre.Color }).ToList(),
                Likes = Db.TrackLikes.Count(l => l.TrackId == id),
            };

            var artistUserId = row.ArtistUserId;
            var comments = Db.Comments.AsNoTracking().Where(c => c.TrackId == id && !c.IsHidden && c.User.IsActive)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new
                {
                    c.ParentId, c.Id, c.UserId, c.User.Username, c.User.DisplayName, c.User.AvatarUrl, c.Body, c.CreatedAt,
                })
                .ToList()
                .Select(c => new
                {
                    c.ParentId,
                    Vm = new CommentVm { Id = c.Id, UserId = c.UserId, Username = c.Username, Name = c.DisplayName, AvatarUrl = c.AvatarUrl, Body = c.Body, CreatedAt = c.CreatedAt, IsArtist = c.UserId == artistUserId },
                })
                .ToList();
            var byId = comments.Where(c => c.ParentId == null).ToDictionary(c => c.Vm.Id, c => c.Vm);
            foreach (var c in comments.Where(c => c.ParentId != null))
            {
                CommentVm parent;
                if (byId.TryGetValue(c.ParentId.Value, out parent)) parent.Replies.Add(c.Vm);
            }
            vm.Comments = byId.Values.OrderByDescending(c => c.CreatedAt).ToList();
            vm.CommentCount = vm.Comments.Count + vm.Comments.Sum(c => c.Replies.Count);

            var artistId = track.ArtistId;
            vm.MoreFromArtist = Db.Tracks.AsNoTracking().Published().Where(t => t.ArtistId == artistId && t.Id != id)
                .OrderByDescending(t => t.PlaysCount).Take(5).ToItems().ToList();

            if (uid != null)
            {
                track.Liked = Db.TrackLikes.Any(l => l.UserId == uid && l.TrackId == id);
                vm.MoreFromArtist.WithLikes(Db, uid);
                vm.MyPlaylists = Db.Playlists.AsNoTracking().Where(p => p.OwnerId == uid).OrderBy(p => p.Title)
                    .Select(p => new PlaylistChoice { Id = p.Id, Title = p.Title, HasTrack = p.PlaylistTracks.Any(pt => pt.TrackId == id) }).ToList();
                var link = Db.ShareLinks.AsNoTracking().Where(s => s.TrackId == id && s.CreatedBy == uid).OrderBy(s => s.Id).FirstOrDefault();
                if (link != null)
                {
                    vm.ShareUrl = Request.Url.GetLeftPart(UriPartial.Authority) + "/s/" + link.Token;
                    vm.ShareClicks = link.ClickCount;
                }
            }

            ViewBag.Title = track.Title + " by " + track.Artist;
            ViewBag.Description = "Listen to " + track.Title + " by " + track.Artist + " on Wavelength.";
            ViewBag.Nav = "explore";
            return View(vm);
        }

        [Authorize]
        [HttpPost, Route("tracks/{id:int}/comments")]
        public ActionResult Comment(int id, string body, int? parentId)
        {
            var uid = CurrentUserId.Value;
            var track = Db.Tracks.Include(t => t.Artist).FirstOrDefault(t => t.Id == id && t.Status == TrackStatus.Published);
            if (track == null) return NotFoundView();
            body = (body ?? "").Trim();
            if (body.Length < 1 || body.Length > 1000)
            {
                Flash("Comments need 1 to 1000 characters.", error: true);
                return Redirect("/tracks/" + id + "#comments");
            }
            Comment parent = null;
            if (parentId != null)
            {
                parent = Db.Comments.FirstOrDefault(c => c.Id == parentId && c.TrackId == id);
                if (parent == null) return new HttpStatusCodeResult(400, "Unknown parent comment");
                if (parent.ParentId != null)
                {
                    var top = parent.ParentId.Value;
                    parent = Db.Comments.First(c => c.Id == top); // one level of replies
                }
            }
            var comment = new Comment { UserId = uid, TrackId = id, ParentId = parent == null ? (int?)null : parent.Id, Body = body };
            Db.Comments.Add(comment);
            var name = User.DisplayName() ?? User.Identity.Name;
            if (parent != null && parent.UserId != uid)
                Notes.Add(parent.UserId, "reply", new { name, title = track.Title, track_id = id });
            else if (parent == null && track.Artist.UserId != uid)
                Notes.Add(track.Artist.UserId, "comment", new { name, title = track.Title, track_id = id });
            Db.SaveChanges();
            Flash(parent == null ? "Comment posted." : "Reply posted.");
            return Redirect("/tracks/" + id + "#comment-" + comment.Id);
        }

        private const string TokenAlphabet = "abcdefghijkmnpqrstuvwxyz23456789";

        private static string NewToken()
        {
            var bytes = new byte[8];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return new string(bytes.Select(b => TokenAlphabet[b % TokenAlphabet.Length]).ToArray());
        }

        [Authorize]
        [HttpPost, Route("tracks/{id:int}/share")]
        public ActionResult Share(int id)
        {
            var uid = CurrentUserId.Value;
            if (!Db.Tracks.Any(t => t.Id == id && t.Status == TrackStatus.Published)) return NotFoundView();
            if (!Db.ShareLinks.Any(s => s.TrackId == id && s.CreatedBy == uid))
            {
                string token;
                do token = NewToken();
                while (Db.ShareLinks.Any(s => s.Token == token));
                Db.ShareLinks.Add(new ShareLink { TrackId = id, CreatedBy = uid, Token = token });
                Db.SaveChanges();
            }
            Flash("Share link ready. Copy it and send it anywhere.");
            return Redirect("/tracks/" + id + "#share");
        }
    }
}
