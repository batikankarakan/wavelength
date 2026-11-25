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
    /// <summary>Artist studio (report section 3.3): dashboard, upload, my tracks, edit track, albums, profile.</summary>
    [Authorize(Roles = AppRoles.Artist)]
    [RoutePrefix("studio")]
    public class StudioController : BaseController
    {
        private const int MaxGenres = 3;

        // ---------- helpers ----------

        /// <summary>The signed-in artist's profile. Created on first visit if an artist account has none.</summary>
        private ArtistProfile Me()
        {
            var uid = CurrentUserId.Value;
            var a = Db.ArtistProfiles.FirstOrDefault(x => x.UserId == uid);
            if (a != null) return a;
            var user = Db.Users.Find(uid);
            a = new ArtistProfile { UserId = uid, StageName = user != null ? user.DisplayName : "New artist" };
            Db.ArtistProfiles.Add(a);
            Db.SaveChanges();
            return a;
        }

        private static StudioHeader Header(ArtistProfile a, string active)
        {
            return new StudioHeader { ArtistId = a.Id, StageName = a.StageName, Verified = a.Verified, Active = active };
        }

        private void SetNav(string title)
        {
            ViewBag.Nav = "studio";
            ViewBag.Title = title;
        }

        /// <summary>Tells every active moderator and admin that a track waits in the review queue.</summary>
        private void NotifyStaff(Track t, string artist)
        {
            Notes.AddToStaff("track_pending", new { title = t.Title, artist = artist, track_id = t.Id });
        }

        /// <summary>The newest rejection reason for a track (stored in the track_rejected notification payload).</summary>
        private string LatestRejection(int trackId)
        {
            var uid = CurrentUserId.Value;
            var rows = Db.Notifications.AsNoTracking()
                .Where(n => n.UserId == uid && n.Type == "track_rejected")
                .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
                .Select(n => n.Payload).Take(50).ToList();
            foreach (var p in rows)
            {
                var d = NotificationService.Read(p);
                string id, reason;
                if (d.TryGetValue("track_id", out id) && id == trackId.ToString())
                    return d.TryGetValue("reason", out reason) && reason != "" ? reason : "No reason given.";
            }
            return null;
        }

        private static IQueryable<StudioTrackRow> Rows(IQueryable<Track> q)
        {
            return q.Select(t => new StudioTrackRow
            {
                Id = t.Id, Title = t.Title, Status = t.Status, CoverUrl = t.CoverUrl, DurationSec = t.DurationSec,
                Color = t.TrackGenres.OrderBy(g => g.GenreId).Select(g => g.Genre.Color).FirstOrDefault() ?? "#FFE500",
                Genre = t.TrackGenres.OrderBy(g => g.GenreId).Select(g => g.Genre.Name).FirstOrDefault(),
                Album = t.Album != null ? t.Album.Title : null,
                AlbumId = t.AlbumId,
                Plays = t.PlaysCount, Likes = t.Likes.Count, Comments = t.Comments.Count, CreatedAt = t.CreatedAt,
            });
        }

        private static Dictionary<string, int> StatusCounts(IQueryable<Track> q)
        {
            return q.GroupBy(t => t.Status).Select(g => new { g.Key, C = g.Count() }).ToList().ToDictionary(x => x.Key, x => x.C);
        }

        // ---------- 1. Artist dashboard ----------

        [HttpGet, Route("")]
        public ActionResult Index()
        {
            var a = Me();
            var aid = a.Id;
            var auid = a.UserId;
            var mine = Db.Tracks.AsNoTracking().Where(t => t.ArtistId == aid);
            var since = DateTime.UtcNow.Date.AddDays(-29);

            var vm = new StudioDashboardVm
            {
                Header = Header(a, "dashboard"),
                TotalPlays = mine.Sum(t => (int?)t.PlaysCount) ?? 0,
                Plays30 = Db.ListeningHistory.Count(h => h.Track.ArtistId == aid && h.PlayedAt >= since),
                Likes = Db.TrackLikes.Count(l => l.Track.ArtistId == aid),
                Followers = Db.Follows.Count(f => f.FollowedId == auid),
                ShareClicks = Db.ShareLinks.Where(s => s.Track.ArtistId == aid).Sum(s => (int?)s.ClickCount) ?? 0,
                StatusCounts = StatusCounts(mine),
            };

            // plays per day, grouped in SQL by calendar day
            var daily = Db.ListeningHistory.AsNoTracking()
                .Where(h => h.Track.ArtistId == aid && h.PlayedAt >= since)
                .GroupBy(h => DbFunctions.TruncateTime(h.PlayedAt))
                .Select(g => new { Day = g.Key, C = g.Count() })
                .ToList()
                .Where(x => x.Day.HasValue)
                .ToDictionary(x => x.Day.Value.Date, x => x.C);
            for (var d = 0; d < 30; d++)
            {
                var day = since.AddDays(d);
                int c;
                vm.Daily.Add(new DayCount { Day = day, Count = daily.TryGetValue(day, out c) ? c : 0 });
            }

            var top = mine.Published().OrderByDescending(t => t.PlaysCount).Take(5).ToItems().ToList();
            var ids = top.Select(t => t.Id).ToList();
            var likes = Db.TrackLikes.Where(l => ids.Contains(l.TrackId)).GroupBy(l => l.TrackId)
                .Select(g => new { g.Key, C = g.Count() }).ToList().ToDictionary(x => x.Key, x => x.C);
            vm.TopTracks = top.Select(t =>
            {
                int n;
                return new StudioTopTrack { Track = t, Likes = likes.TryGetValue(t.Id, out n) ? n : 0 };
            }).ToList();

            vm.Comments = Db.Comments.AsNoTracking()
                .Where(c => c.Track.ArtistId == aid && !c.IsHidden && c.UserId != auid)
                .OrderByDescending(c => c.CreatedAt).Take(6)
                .Select(c => new StudioComment
                {
                    TrackId = c.TrackId, TrackTitle = c.Track.Title, User = c.User.DisplayName, Username = c.User.Username,
                    AvatarUrl = c.User.AvatarUrl, Body = c.Body, CreatedAt = c.CreatedAt,
                }).ToList();

            SetNav("Artist studio");
            return View(vm);
        }

        // ---------- 2. Upload track ----------

        private TrackFormVm FormVm(ArtistProfile a, TrackForm form, string active)
        {
            var aid = a.Id;
            return new TrackFormVm
            {
                Header = Header(a, active),
                Form = form,
                Genres = Db.Genres.AsNoTracking().OrderBy(g => g.Name).ToList(),
                Albums = Db.Albums.AsNoTracking().Where(x => x.ArtistId == aid).OrderByDescending(x => x.ReleaseDate)
                    .Select(x => new AlbumOption { Id = x.Id, Title = x.Title }).ToList(),
            };
        }

        private void ValidateCommon(ArtistProfile a, TrackForm f)
        {
            f.GenreIds = (f.GenreIds ?? new List<int>()).Distinct().ToList();
            var genreIds = f.GenreIds;
            if (genreIds.Count == 0) ModelState.AddModelError("Form.GenreIds", "Pick at least one genre.");
            else if (genreIds.Count > MaxGenres) ModelState.AddModelError("Form.GenreIds", "Pick at most " + MaxGenres + " genres.");
            else if (Db.Genres.Count(g => genreIds.Contains(g.Id)) != genreIds.Count)
                ModelState.AddModelError("Form.GenreIds", "Unknown genre.");
            var albumId = f.AlbumId;
            var aid = a.Id;
            if (albumId != null && !Db.Albums.Any(x => x.Id == albumId && x.ArtistId == aid))
                ModelState.AddModelError("Form.AlbumId", "Choose one of your albums.");
            var coverErr = FileStorage.ValidateImage(f.Cover);
            if (coverErr != null) ModelState.AddModelError("Form.Cover", coverErr);
        }

        [HttpGet, Route("upload")]
        public ActionResult Upload()
        {
            var a = Me();
            SetNav("Upload track");
            return View("TrackForm", FormVm(a, new TrackForm(), "upload"));
        }

        [HttpPost, Route("upload")]
        public ActionResult Upload([Bind(Prefix = "Form")] TrackForm f, string intent)
        {
            if (f == null) { f = new TrackForm(); TryValidateModel(f, "Form"); } // nothing posted
            var a = Me();
            f.Id = null;
            ValidateCommon(a, f);
            var audioErr = FileStorage.ValidateAudio(f.Audio);
            if (audioErr != null) ModelState.AddModelError("Form.Audio", audioErr);
            else if (f.DurationSec < 1) ModelState.AddModelError("Form.Audio", "We could not read the length of this audio file. Try another file.");

            if (!ModelState.IsValid)
            {
                SetNav("Upload track");
                return View("TrackForm", FormVm(a, f, "upload"));
            }

            var submit = intent == "submit";
            var track = new Track
            {
                ArtistId = a.Id, AlbumId = f.AlbumId, Title = f.Title.Trim(), Lyrics = string.IsNullOrWhiteSpace(f.Lyrics) ? null : f.Lyrics.Trim(),
                DurationSec = Math.Max(1, Math.Min(f.DurationSec, 3600)),
                AudioUrl = Files.Save(f.Audio, "audio"),
                CoverUrl = FileStorage.HasFile(f.Cover) ? Files.Save(f.Cover, "covers") : null,
                Status = submit ? TrackStatus.Pending : TrackStatus.Draft,
            };
            foreach (var g in f.GenreIds) track.TrackGenres.Add(new TrackGenre { GenreId = g });
            Db.Tracks.Add(track);
            Db.SaveChanges();
            if (submit)
            {
                NotifyStaff(track, a.StageName);
                Db.SaveChanges();
            }
            Flash(submit ? "\"" + track.Title + "\" is in the review queue. We'll notify you when it's live." : "\"" + track.Title + "\" saved as a draft.");
            return Redirect("/studio/tracks");
        }

        // ---------- 3. My tracks ----------

        [HttpGet, Route("tracks")]
        public ActionResult Tracks(string status)
        {
            var a = Me();
            var aid = a.Id;
            var filter = status != null && TrackStatus.All.Contains(status) ? status : "all";
            var mine = Db.Tracks.AsNoTracking().Where(t => t.ArtistId == aid);
            var counts = StatusCounts(mine);
            counts["all"] = counts.Values.Sum();
            var q = filter == "all" ? mine : mine.Where(t => t.Status == filter);
            var vm = new StudioTracksVm
            {
                Header = Header(a, "tracks"),
                Filter = filter,
                Counts = counts,
                Tracks = Rows(q.OrderByDescending(t => t.CreatedAt)).ToList(),
            };
            SetNav("My tracks");
            return View(vm);
        }

        [HttpPost, Route("tracks/{id:int}/submit")]
        public ActionResult Submit(int id, string returnUrl)
        {
            var a = Me();
            var aid = a.Id;
            var t = Db.Tracks.FirstOrDefault(x => x.Id == id && x.ArtistId == aid);
            if (t == null) return NotFoundView();
            if (t.Status == TrackStatus.Draft || t.Status == TrackStatus.Rejected)
            {
                t.Status = TrackStatus.Pending;
                NotifyStaff(t, a.StageName);
                Db.SaveChanges();
                Flash("\"" + t.Title + "\" was sent for review.");
            }
            else Flash("Only drafts and rejected tracks can be submitted for review.", true);
            return SafeRedirect(returnUrl, "/studio/tracks");
        }

        [HttpPost, Route("tracks/{id:int}/delete")]
        public ActionResult Delete(int id)
        {
            var a = Me();
            var aid = a.Id;
            var t = Db.Tracks.AsNoTracking().FirstOrDefault(x => x.Id == id && x.ArtistId == aid);
            if (t == null) return NotFoundView();
            if (t.Status == TrackStatus.Pending || t.Status == TrackStatus.Hidden)
            {
                Flash(t.Status == TrackStatus.Pending ? "This track is being reviewed. Wait for the decision before deleting it." : "Hidden tracks are under moderation and cannot be deleted.", true);
                return Redirect("/studio/tracks");
            }
            Cleanup.DeleteTrack(t.Id);
            Files.Delete(t.AudioUrl);
            Files.Delete(t.CoverUrl);
            Flash("\"" + t.Title + "\" was deleted.");
            return Redirect("/studio/tracks");
        }

        // ---------- 4. Edit track ----------

        private TrackFormVm EditVm(ArtistProfile a, int id, TrackForm posted)
        {
            var aid = a.Id;
            var t = Db.Tracks.AsNoTracking().Include(x => x.TrackGenres).FirstOrDefault(x => x.Id == id && x.ArtistId == aid);
            if (t == null) return null;
            var form = posted ?? new TrackForm
            {
                Id = t.Id, Title = t.Title, Lyrics = t.Lyrics, AlbumId = t.AlbumId, DurationSec = t.DurationSec,
                GenreIds = t.TrackGenres.Select(g => g.GenreId).ToList(),
            };
            form.Id = t.Id;
            if (form.GenreIds == null) form.GenreIds = new List<int>();
            var vm = FormVm(a, form, "tracks");
            vm.Status = t.Status;
            vm.AudioUrl = t.AudioUrl;
            vm.CoverUrl = t.CoverUrl;
            if (t.Status == TrackStatus.Rejected) vm.RejectionReason = LatestRejection(t.Id);
            return vm;
        }

        [HttpGet, Route("tracks/{id:int}")]
        public ActionResult Edit(int id)
        {
            var a = Me();
            var vm = EditVm(a, id, null);
            if (vm == null) return NotFoundView();
            SetNav("Edit " + vm.Form.Title);
            return View("TrackForm", vm);
        }

        [HttpPost, Route("tracks/{id:int}")]
        public ActionResult Edit(int id, [Bind(Prefix = "Form")] TrackForm f, string intent)
        {
            if (f == null) { f = new TrackForm(); TryValidateModel(f, "Form"); } // nothing posted
            var a = Me();
            var aid = a.Id;
            var t = Db.Tracks.Include(x => x.TrackGenres).FirstOrDefault(x => x.Id == id && x.ArtistId == aid);
            if (t == null) return NotFoundView();

            ValidateCommon(a, f);
            var newAudio = FileStorage.HasFile(f.Audio);
            if (newAudio)
            {
                var err = FileStorage.ValidateAudio(f.Audio);
                if (err != null) ModelState.AddModelError("Form.Audio", err);
                else if (f.DurationSec < 1) ModelState.AddModelError("Form.Audio", "We could not read the length of this audio file.");
            }
            if (!ModelState.IsValid)
            {
                var vm = EditVm(a, id, f);
                SetNav("Edit " + t.Title);
                return View("TrackForm", vm);
            }

            t.Title = f.Title.Trim();
            t.Lyrics = string.IsNullOrWhiteSpace(f.Lyrics) ? null : f.Lyrics.Trim();
            t.AlbumId = f.AlbumId;
            var removed = t.TrackGenres.Where(g => !f.GenreIds.Contains(g.GenreId)).ToList();
            Db.TrackGenres.RemoveRange(removed);
            foreach (var g in f.GenreIds.Where(g => t.TrackGenres.All(x => x.GenreId != g)).ToList())
                t.TrackGenres.Add(new TrackGenre { TrackId = t.Id, GenreId = g });

            if (FileStorage.HasFile(f.Cover))
            {
                Files.Delete(t.CoverUrl);
                t.CoverUrl = Files.Save(f.Cover, "covers");
            }
            else if (f.RemoveCover)
            {
                Files.Delete(t.CoverUrl);
                t.CoverUrl = null;
            }
            if (newAudio)
            {
                Files.Delete(t.AudioUrl);
                t.AudioUrl = Files.Save(f.Audio, "audio");
                t.DurationSec = Math.Max(1, Math.Min(f.DurationSec, 3600));
            }

            // State machine (report figure 6)
            var before = t.Status;
            var wantsReview = intent == "submit";
            string msg;
            if (before == TrackStatus.Draft)
            {
                t.Status = wantsReview ? TrackStatus.Pending : TrackStatus.Draft;
                msg = wantsReview ? "Sent for review." : "Draft saved.";
            }
            else if (before == TrackStatus.Rejected)
            {
                // "artist edits the track" moves a rejected track back to draft, or straight to review
                t.Status = wantsReview ? TrackStatus.Pending : TrackStatus.Draft;
                msg = wantsReview ? "Resubmitted for review." : "Saved. The track is a draft again; submit it when it's ready.";
            }
            else if (before == TrackStatus.Published && newAudio)
            {
                t.Status = TrackStatus.Pending;
                msg = "New audio uploaded. The track goes back to the review queue until a moderator approves it.";
            }
            else
            {
                msg = "Changes saved.";
            }
            Db.SaveChanges();
            if (t.Status == TrackStatus.Pending && before != TrackStatus.Pending)
            {
                NotifyStaff(t, a.StageName);
                Db.SaveChanges();
            }
            Flash(msg);
            return Redirect("/studio/tracks/" + t.Id);
        }

        // ---------- 5. My albums ----------

        private StudioAlbumsVm AlbumsVm(ArtistProfile a, AlbumForm newAlbum)
        {
            var aid = a.Id;
            var albums = Db.Albums.AsNoTracking().Where(x => x.ArtistId == aid).OrderByDescending(x => x.ReleaseDate)
                .Select(x => new StudioAlbumRow { Id = x.Id, Title = x.Title, Type = x.Type, ReleaseDate = x.ReleaseDate, CoverUrl = x.CoverUrl })
                .ToList();
            var tracks = Rows(Db.Tracks.AsNoTracking().Where(t => t.ArtistId == aid).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)).ToList();
            foreach (var al in albums) al.Tracks = tracks.Where(t => t.AlbumId == al.Id).ToList();
            return new StudioAlbumsVm
            {
                Header = Header(a, "albums"),
                Albums = albums,
                Singles = tracks.Where(t => t.AlbumId == null).ToList(),
                NewAlbum = newAlbum ?? new AlbumForm(),
            };
        }

        private void ValidateAlbum(AlbumForm f, string prefix)
        {
            if (f.Type != "single" && f.Type != "ep" && f.Type != "album") ModelState.AddModelError(prefix + "Type", "Choose single, EP or album.");
            if (f.ReleaseDate.Year < 1900 || f.ReleaseDate > DateTime.UtcNow.AddYears(2))
                ModelState.AddModelError(prefix + "ReleaseDate", "Enter a realistic release date.");
            var err = FileStorage.ValidateImage(f.Cover);
            if (err != null) ModelState.AddModelError(prefix + "Cover", err);
        }

        [HttpGet, Route("albums")]
        public ActionResult Albums()
        {
            var a = Me();
            SetNav("My albums");
            return View(AlbumsVm(a, null));
        }

        [HttpPost, Route("albums")]
        public ActionResult CreateAlbum([Bind(Prefix = "NewAlbum")] AlbumForm f)
        {
            if (f == null) { f = new AlbumForm(); TryValidateModel(f, "NewAlbum"); } // nothing posted
            var a = Me();
            ValidateAlbum(f, "NewAlbum.");
            if (!ModelState.IsValid)
            {
                SetNav("My albums");
                ViewBag.OpenCreate = true;
                return View("Albums", AlbumsVm(a, f));
            }
            var album = new Album
            {
                ArtistId = a.Id, Title = f.Title.Trim(), Type = f.Type, ReleaseDate = f.ReleaseDate.Date,
                CoverUrl = FileStorage.HasFile(f.Cover) ? Files.Save(f.Cover, "covers") : null,
            };
            Db.Albums.Add(album);
            Db.SaveChanges();
            Flash("Album \"" + album.Title + "\" created. Now add some tracks to it.");
            return Redirect("/studio/albums#album-" + album.Id);
        }

        [HttpPost, Route("albums/{id:int}")]
        public ActionResult UpdateAlbum(int id, AlbumForm f)
        {
            if (f == null) { f = new AlbumForm(); TryValidateModel(f); } // nothing posted
            var a = Me();
            var aid = a.Id;
            var album = Db.Albums.FirstOrDefault(x => x.Id == id && x.ArtistId == aid);
            if (album == null) return NotFoundView();
            ValidateAlbum(f, "");
            if (!ModelState.IsValid)
            {
                Flash(ModelState.Values.SelectMany(v => v.Errors).First().ErrorMessage, true);
                return Redirect("/studio/albums#album-" + id);
            }
            album.Title = f.Title.Trim();
            album.Type = f.Type;
            album.ReleaseDate = f.ReleaseDate.Date;
            if (FileStorage.HasFile(f.Cover))
            {
                Files.Delete(album.CoverUrl);
                album.CoverUrl = Files.Save(f.Cover, "covers");
            }
            Db.SaveChanges();
            Flash("Album updated.");
            return Redirect("/studio/albums#album-" + id);
        }

        [HttpPost, Route("albums/{id:int}/delete")]
        public ActionResult DeleteAlbum(int id)
        {
            var a = Me();
            var aid = a.Id;
            var album = Db.Albums.AsNoTracking().FirstOrDefault(x => x.Id == id && x.ArtistId == aid);
            if (album == null) return NotFoundView();
            Cleanup.DeleteAlbum(id);
            Files.Delete(album.CoverUrl);
            Flash("Album \"" + album.Title + "\" deleted. Its tracks are now singles.");
            return Redirect("/studio/albums");
        }

        [HttpPost, Route("albums/{id:int}/tracks")]
        public ActionResult AddToAlbum(int id, int trackId)
        {
            var a = Me();
            var aid = a.Id;
            var album = Db.Albums.Any(x => x.Id == id && x.ArtistId == aid);
            var t = Db.Tracks.FirstOrDefault(x => x.Id == trackId && x.ArtistId == aid);
            if (!album || t == null) return NotFoundView();
            t.AlbumId = id;
            Db.SaveChanges();
            Flash("\"" + t.Title + "\" added to the album.");
            return Redirect("/studio/albums#album-" + id);
        }

        [HttpPost, Route("albums/{id:int}/tracks/{trackId:int}/remove")]
        public ActionResult RemoveFromAlbum(int id, int trackId)
        {
            var a = Me();
            var aid = a.Id;
            var t = Db.Tracks.FirstOrDefault(x => x.Id == trackId && x.ArtistId == aid && x.AlbumId == id);
            if (t == null) return NotFoundView();
            t.AlbumId = null;
            Db.SaveChanges();
            Flash("\"" + t.Title + "\" removed from the album.");
            return Redirect("/studio/albums#album-" + id);
        }

        // ---------- 6. Artist profile settings ----------

        private StudioProfileVm ProfileVm(ArtistProfile a, ArtistProfileForm f)
        {
            var auid = a.UserId;
            return new StudioProfileVm
            {
                Header = Header(a, "profile"),
                Form = f ?? new ArtistProfileForm { StageName = a.StageName, Country = a.Country, Website = a.Website },
                BannerUrl = a.BannerUrl,
                AvatarUrl = Db.Users.Where(u => u.Id == auid).Select(u => u.AvatarUrl).FirstOrDefault(),
                Followers = Db.Follows.Count(x => x.FollowedId == auid),
            };
        }

        [HttpGet, Route("profile"), ActionName("Profile")]
        public ActionResult EditProfile()
        {
            var a = Me();
            SetNav("Artist profile");
            return View("Profile", ProfileVm(a, null));
        }

        [HttpPost, Route("profile"), ActionName("Profile")]
        public ActionResult SaveProfile([Bind(Prefix = "Form")] ArtistProfileForm f)
        {
            if (f == null) { f = new ArtistProfileForm(); TryValidateModel(f, "Form"); } // nothing posted
            var a = Me();
            var err = FileStorage.ValidateImage(f.Banner);
            if (err != null) ModelState.AddModelError("Form.Banner", err);
            if (!ModelState.IsValid)
            {
                SetNav("Artist profile");
                return View("Profile", ProfileVm(a, f));
            }
            a.StageName = f.StageName.Trim();
            a.Country = string.IsNullOrWhiteSpace(f.Country) ? null : f.Country.Trim();
            a.Website = string.IsNullOrWhiteSpace(f.Website) ? null : f.Website.Trim();
            if (FileStorage.HasFile(f.Banner))
            {
                Files.Delete(a.BannerUrl);
                a.BannerUrl = Files.Save(f.Banner, "banners");
            }
            else if (f.RemoveBanner)
            {
                Files.Delete(a.BannerUrl);
                a.BannerUrl = null;
            }
            Db.SaveChanges();
            Flash("Artist profile saved.");
            return Redirect("/studio/profile");
        }
    }
}
