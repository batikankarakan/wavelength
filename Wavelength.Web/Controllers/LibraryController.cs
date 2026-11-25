using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Models;
using Wavelength.Web.Services;

namespace Wavelength.Web.Controllers
{
    /// <summary>Listener pages 1, 2, 4, 5, 6: library, my playlists, history, following, notifications.</summary>
    [Authorize]
    public class LibraryController : BaseController
    {
        private int Me { get { return CurrentUserId.Value; } }

        // ---------- 1. My Library ----------
        [HttpGet, Route("library")]
        public ActionResult Index(string tab)
        {
            var me = Me;
            var vm = new LibraryVm { Tab = tab == "albums" || tab == "playlists" ? tab : "liked" };
            vm.LikedCount = Db.TrackLikes.Count(l => l.UserId == me && l.Track.Status == TrackStatus.Published);
            vm.AlbumCount = Db.SavedAlbums.Count(s => s.UserId == me);
            vm.PlaylistCount = Db.SavedPlaylists.Count(s => s.UserId == me && (s.Playlist.IsPublic || s.Playlist.OwnerId == me));

            if (vm.Tab == "liked")
            {
                var ids = Db.TrackLikes.Where(l => l.UserId == me).OrderByDescending(l => l.CreatedAt).Select(l => l.TrackId).ToList();
                var items = Db.Tracks.AsNoTracking().Published().Where(t => ids.Contains(t.Id)).ToItems().ToList();
                vm.Liked = items.OrderBy(i => ids.IndexOf(i.Id)).ToList();
                foreach (var i in vm.Liked) i.Liked = true;
            }
            else if (vm.Tab == "albums")
            {
                var ids = Db.SavedAlbums.Where(s => s.UserId == me).OrderByDescending(s => s.SavedAt).Select(s => s.AlbumId).ToList();
                var items = Db.Albums.AsNoTracking().Where(a => ids.Contains(a.Id)).ToItems().ToList();
                vm.Albums = items.OrderBy(i => ids.IndexOf(i.Id)).ToList();
            }
            else
            {
                var ids = Db.SavedPlaylists.Where(s => s.UserId == me && (s.Playlist.IsPublic || s.Playlist.OwnerId == me))
                    .OrderByDescending(s => s.SavedAt).Select(s => s.PlaylistId).ToList();
                var items = Db.Playlists.AsNoTracking().Where(p => ids.Contains(p.Id)).ToItems().ToList();
                vm.Playlists = items.OrderBy(i => ids.IndexOf(i.Id)).ToList();
            }
            return View(vm);
        }

        // ---------- 2. My Playlists ----------
        [HttpGet, Route("library/playlists")]
        public ActionResult Playlists()
        {
            var me = Me;
            var vm = new MyPlaylistsVm
            {
                Playlists = Db.Playlists.AsNoTracking().Where(p => p.OwnerId == me).OrderByDescending(p => p.CreatedAt).ToItems().ToList(),
            };
            return View(vm);
        }

        // ---------- 4. Listening History ----------
        [HttpGet, Route("history")]
        public ActionResult History(string range, string genre, int page = 1)
        {
            var me = Me;
            var vm = new HistoryVm
            {
                Range = range == "today" || range == "week" || range == "month" ? range : "all",
                Genre = string.IsNullOrWhiteSpace(genre) ? null : genre,
            };
            vm.Genres = Db.Genres.AsNoTracking().OrderBy(g => g.Name).ToList();

            var q = Db.ListeningHistory.AsNoTracking().Where(h => h.UserId == me);
            var now = DateTime.UtcNow;
            if (vm.Range == "today") { var from = now.Date; q = q.Where(h => h.PlayedAt >= from); }
            else if (vm.Range == "week") { var from = now.AddDays(-7); q = q.Where(h => h.PlayedAt >= from); }
            else if (vm.Range == "month") { var from = now.AddDays(-30); q = q.Where(h => h.PlayedAt >= from); }
            if (vm.Genre != null)
            {
                var slug = vm.Genre;
                q = q.Where(h => h.Track.TrackGenres.Any(g => g.Genre.Slug == slug));
            }

            vm.TotalPlays = q.Count();
            vm.TotalMinutes = (int)Math.Round((q.Sum(h => (long?)h.SecondsListened) ?? 0) / 60.0);
            vm.UniqueTracks = q.Select(h => h.TrackId).Distinct().Count();

            const int size = 40;
            vm.TotalPages = Math.Max(1, (int)Math.Ceiling(vm.TotalPlays / (double)size));
            vm.Page = Math.Max(1, Math.Min(page, vm.TotalPages));
            var skip = (vm.Page - 1) * size;
            var rows = q.OrderByDescending(h => h.PlayedAt).Skip(skip).Take(size)
                .Select(h => new { h.Id, h.PlayedAt, h.SecondsListened, h.TrackId }).ToList();
            var trackIds = rows.Select(r => r.TrackId).Distinct().ToList();
            var trackList = Db.Tracks.AsNoTracking().Where(t => trackIds.Contains(t.Id)).ToItems().ToList();
            trackList.MarkLiked(Db, me);
            var tracks = trackList.ToDictionary(t => t.Id);

            vm.Days = rows.Where(r => tracks.ContainsKey(r.TrackId))
                .Select(r => new HistoryEntry { Id = r.Id, PlayedAt = r.PlayedAt, SecondsListened = r.SecondsListened, Track = tracks[r.TrackId] })
                .GroupBy(e => e.PlayedAt.Date)
                .Select(g => new HistoryDay { Day = g.Key, Entries = g.ToList() })
                .ToList();
            return View(vm);
        }

        [HttpPost, Route("history/clear")]
        public ActionResult ClearHistory()
        {
            var n = Db.Database.ExecuteSqlCommand("DELETE FROM listening_history WHERE user_id = @p0", Me);
            Flash(n == 0 ? "Your history was already empty." : "Cleared " + n + " plays from your history.");
            return Redirect("/history");
        }

        // ---------- 5. Following ----------
        [HttpGet, Route("following")]
        public ActionResult Following(string tab)
        {
            var me = Me;
            var vm = new FollowingVm { Tab = tab == "followers" ? "followers" : "following" };
            vm.FollowingCount = Db.Follows.Count(f => f.FollowerId == me);
            vm.FollowersCount = Db.Follows.Count(f => f.FollowedId == me);

            var myFollowing = new HashSet<int>(Db.Follows.Where(f => f.FollowerId == me).Select(f => f.FollowedId).ToList());
            if (vm.Tab == "following")
            {
                vm.People = Db.Follows.AsNoTracking().Where(f => f.FollowerId == me).OrderByDescending(f => f.CreatedAt)
                    .Select(f => new FollowPerson
                    {
                        UserId = f.Followed.Id,
                        Username = f.Followed.Username,
                        DisplayName = f.Followed.DisplayName,
                        AvatarUrl = f.Followed.AvatarUrl,
                        ArtistId = f.Followed.ArtistProfiles.Select(a => (int?)a.Id).FirstOrDefault(),
                        StageName = f.Followed.ArtistProfiles.Select(a => a.StageName).FirstOrDefault(),
                        Verified = f.Followed.ArtistProfiles.Any(a => a.Verified),
                        Followers = f.Followed.Followers.Count,
                        Since = f.CreatedAt,
                    }).ToList();
            }
            else
            {
                vm.People = Db.Follows.AsNoTracking().Where(f => f.FollowedId == me).OrderByDescending(f => f.CreatedAt)
                    .Select(f => new FollowPerson
                    {
                        UserId = f.Follower.Id,
                        Username = f.Follower.Username,
                        DisplayName = f.Follower.DisplayName,
                        AvatarUrl = f.Follower.AvatarUrl,
                        ArtistId = f.Follower.ArtistProfiles.Select(a => (int?)a.Id).FirstOrDefault(),
                        StageName = f.Follower.ArtistProfiles.Select(a => a.StageName).FirstOrDefault(),
                        Verified = f.Follower.ArtistProfiles.Any(a => a.Verified),
                        Followers = f.Follower.Followers.Count,
                        Since = f.CreatedAt,
                    }).ToList();
            }
            foreach (var p in vm.People) p.IFollow = myFollowing.Contains(p.UserId);
            return View(vm);
        }

        // ---------- 6. Notifications ----------
        [HttpGet, Route("notifications")]
        public ActionResult Notifications(string filter, int page = 1)
        {
            var me = Me;
            var vm = new NotificationsVm { UnreadOnly = filter == "unread" };
            vm.Unread = Db.Notifications.Count(n => n.UserId == me && !n.IsRead);
            var q = Db.Notifications.AsNoTracking().Where(n => n.UserId == me);
            if (vm.UnreadOnly) q = q.Where(n => !n.IsRead);
            const int size = 25;
            var total = q.Count();
            vm.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)size));
            vm.Page = Math.Max(1, Math.Min(page, vm.TotalPages));
            var skip = (vm.Page - 1) * size;
            var rows = q.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip(skip).Take(size).ToList();
            vm.Items = rows.Select(n =>
            {
                var d = NotificationService.Describe(n);
                return new NotificationRow { Id = n.Id, Type = n.Type, Text = d.Item1, Link = d.Item2, IsRead = n.IsRead, CreatedAt = n.CreatedAt };
            }).ToList();
            return View(vm);
        }

        [HttpPost, Route("notifications/{id:int}/read")]
        public ActionResult MarkRead(int id, string returnUrl)
        {
            var me = Me;
            var n = Db.Notifications.FirstOrDefault(x => x.Id == id && x.UserId == me);
            if (n == null) return NotFoundView();
            n.IsRead = true;
            Db.SaveChanges();
            return SafeRedirect(returnUrl, "/notifications");
        }

        /// <summary>Marks a notification read and follows its link.</summary>
        [HttpPost, Route("notifications/{id:int}/open")]
        public ActionResult Open(int id)
        {
            var me = Me;
            var n = Db.Notifications.FirstOrDefault(x => x.Id == id && x.UserId == me);
            if (n == null) return NotFoundView();
            n.IsRead = true;
            Db.SaveChanges();
            return SafeRedirect(NotificationService.Describe(n).Item2, "/notifications");
        }

        [HttpPost, Route("notifications/read-all")]
        public ActionResult ReadAll()
        {
            var n = Db.Database.ExecuteSqlCommand("UPDATE notifications SET is_read = 1 WHERE user_id = @p0 AND is_read = 0", Me);
            Flash(n == 0 ? "Nothing new to mark." : "Marked " + n + " notifications as read.");
            return Redirect("/notifications");
        }
    }
}
