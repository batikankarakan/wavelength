using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Models;

namespace Wavelength.Web.Controllers
{
    /// <summary>Public pages 7, 8, 12 and 13: Explore, Search (AJAX), Genre page, Charts.</summary>
    public class DiscoverController : BaseController
    {
        private List<GenreItem> GenreItems()
        {
            return Db.Genres.AsNoTracking().OrderBy(g => g.Name).Select(g => new GenreItem
            {
                Id = g.Id, Name = g.Name, Slug = g.Slug, Color = g.Color,
                TrackCount = g.TrackGenres.Count(tg => tg.Track.Status == TrackStatus.Published),
            }).ToList();
        }

        private IQueryable<ArtistProfile> ActiveArtists { get { return Db.ArtistProfiles.AsNoTracking().Where(a => a.User.IsActive); } }

        [HttpGet, Route("explore")]
        public ActionResult Explore()
        {
            var uid = CurrentUserId;
            var saved = Db.SavedPlaylists;
            var vm = new ExploreVm
            {
                Genres = GenreItems(),
                NewReleases = Db.Tracks.AsNoTracking().Published().OrderByDescending(t => t.CreatedAt).Take(8).ToItems().ToList(),
                Artists = ActiveArtists.ToItems().OrderByDescending(a => a.Plays).Take(8).ToList(),
                Albums = Db.Albums.AsNoTracking().ToItems().Where(a => a.TrackCount > 0).OrderByDescending(a => a.ReleaseDate).Take(4).ToList(),
                Playlists = Db.Playlists.AsNoTracking().Where(p => p.IsPublic && p.Owner.IsActive)
                    .OrderByDescending(p => saved.Count(s => s.PlaylistId == p.Id)).ThenByDescending(p => p.PlaylistTracks.Count)
                    .ToItems().Take(4).ToList(),
            };

            // Recommendations: published tracks from the genres the user listens to most, which they have played least.
            if (uid != null)
            {
                var since = DateTime.UtcNow.AddDays(-60);
                var history = Db.ListeningHistory;
                var topGenres = history.Where(h => h.UserId == uid && h.PlayedAt > since)
                    .SelectMany(h => h.Track.TrackGenres.Select(g => g.GenreId))
                    .GroupBy(g => g).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(3).ToList();
                if (topGenres.Count > 0)
                {
                    vm.Personal = true;
                    vm.ForYou = Db.Tracks.AsNoTracking().Published()
                        .Where(t => t.TrackGenres.Any(g => topGenres.Contains(g.GenreId)))
                        .OrderBy(t => history.Count(h => h.UserId == uid && h.TrackId == t.Id))
                        .ThenByDescending(t => t.PlaysCount).Take(8).ToItems().ToList();
                }
            }
            if (vm.ForYou.Count == 0)
                vm.ForYou = Db.Tracks.AsNoTracking().Published().OrderByDescending(t => t.PlaysCount).Take(8).ToItems().ToList();
            vm.ForYou.WithLikes(Db, uid);

            ViewBag.Nav = "explore";
            return View(vm);
        }

        private SearchVm RunSearch(string q, string type, string genre, string sort)
        {
            var uid = CurrentUserId;
            var vm = new SearchVm
            {
                Q = (q ?? "").Trim(),
                Type = type == "tracks" || type == "artists" || type == "albums" || type == "playlists" ? type : "all",
                Genre = string.IsNullOrWhiteSpace(genre) ? null : genre.Trim(),
                Sort = sort == "new" || sort == "az" ? sort : "popular",
                Genres = GenreItems(),
            };
            var term = vm.Q;
            var slug = vm.Genre;
            var all = vm.Type == "all";
            Func<int, int> take = few => all ? few : 40;
            var hasQuery = vm.HasFilter;

            if (all || vm.Type == "tracks")
            {
                var tq = Db.Tracks.AsNoTracking().Published();
                if (term != "") tq = tq.Where(t => t.Title.Contains(term) || t.Artist.StageName.Contains(term) || (t.Album != null && t.Album.Title.Contains(term)) || (t.Lyrics != null && t.Lyrics.Contains(term)));
                if (slug != null) tq = tq.Where(t => t.TrackGenres.Any(g => g.Genre.Slug == slug));
                if (vm.Sort == "new") tq = tq.OrderByDescending(t => t.CreatedAt);
                else if (vm.Sort == "az") tq = tq.OrderBy(t => t.Title);
                else tq = tq.OrderByDescending(t => t.PlaysCount);
                vm.Tracks = tq.Take(take(hasQuery ? 10 : 6)).ToItems().ToList().WithLikes(Db, uid);
            }
            if (all || vm.Type == "artists")
            {
                var aq = ActiveArtists;
                if (term != "") aq = aq.Where(a => a.StageName.Contains(term) || (a.Country != null && a.Country.Contains(term)));
                if (slug != null) aq = aq.Where(a => a.Tracks.Any(t => t.Status == TrackStatus.Published && t.TrackGenres.Any(g => g.Genre.Slug == slug)));
                var items = aq.ToItems();
                items = vm.Sort == "az" ? items.OrderBy(a => a.StageName) : items.OrderByDescending(a => a.Plays);
                vm.Artists = items.Take(take(4)).ToList();
            }
            if (all || vm.Type == "albums")
            {
                var alq = Db.Albums.AsNoTracking().Where(a => a.Tracks.Any(t => t.Status == TrackStatus.Published));
                if (term != "") alq = alq.Where(a => a.Title.Contains(term) || a.Artist.StageName.Contains(term));
                if (slug != null) alq = alq.Where(a => a.Tracks.Any(t => t.Status == TrackStatus.Published && t.TrackGenres.Any(g => g.Genre.Slug == slug)));
                var items = alq.ToItems();
                if (vm.Sort == "az") items = items.OrderBy(a => a.Title);
                else if (vm.Sort == "new") items = items.OrderByDescending(a => a.ReleaseDate);
                else items = items.OrderByDescending(a => a.TrackCount);
                vm.Albums = items.Take(take(4)).ToList();
            }
            if (all || vm.Type == "playlists")
            {
                var pq = Db.Playlists.AsNoTracking().Where(p => p.IsPublic && p.Owner.IsActive);
                if (term != "") pq = pq.Where(p => p.Title.Contains(term) || (p.Description != null && p.Description.Contains(term)) || p.Owner.DisplayName.Contains(term));
                if (slug != null) pq = pq.Where(p => p.PlaylistTracks.Any(pt => pt.Track.TrackGenres.Any(g => g.Genre.Slug == slug)));
                if (vm.Sort == "az") pq = pq.OrderBy(p => p.Title);
                else if (vm.Sort == "new") pq = pq.OrderByDescending(p => p.CreatedAt);
                else pq = pq.OrderByDescending(p => p.PlaylistTracks.Count);
                vm.Playlists = pq.ToItems().Take(take(4)).ToList();
            }
            return vm;
        }

        [HttpGet, Route("search")]
        public ActionResult Search(string q, string type, string genre, string sort)
        {
            var vm = RunSearch(q, type, genre, sort);
            ViewBag.Nav = "search";
            ViewBag.Title = vm.Q == "" ? "Search" : "Search: " + vm.Q;
            return View("Search", vm);
        }

        /// <summary>
        /// Target of the Ajax.BeginForm on the search page: returns only the results partial for AJAX requests
        /// (jQuery unobtrusive AJAX), or the full page when JavaScript is off.
        /// </summary>
        [HttpGet, Route("search/results")]
        public ActionResult SearchResults(string q, string type, string genre, string sort)
        {
            if (!Request.IsAjaxRequest()) return Search(q, type, genre, sort);
            var vm = RunSearch(q, type, genre, sort);
            return PartialView("_SearchResults", vm);
        }

        /// <summary>/genres/{slug} — routed by the "Genre" route in RouteConfig.</summary>
        [HttpGet]
        public ActionResult Genre(string slug, string sort, int page = 1)
        {
            var all = GenreItems();
            var g = all.FirstOrDefault(x => x.Slug == slug);
            if (g == null) return NotFoundView();
            const int size = 20;
            var genreId = g.Id;
            var q = Db.Tracks.AsNoTracking().Published().Where(t => t.TrackGenres.Any(x => x.GenreId == genreId));
            var total = q.Count();
            var vm = new GenrePageVm
            {
                Genre = g, Sort = sort == "new" ? "new" : "popular", Others = all.Where(x => x.Id != g.Id).ToList(),
                TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)size)),
            };
            vm.Page = Math.Max(1, Math.Min(page, vm.TotalPages));
            vm.Offset = (vm.Page - 1) * size;
            q = vm.Sort == "new" ? q.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id) : q.OrderByDescending(t => t.PlaysCount).ThenBy(t => t.Id);
            vm.Tracks = q.Skip(vm.Offset).Take(size).ToItems().ToList().WithLikes(Db, CurrentUserId);
            vm.Artists = ActiveArtists.Where(a => a.Tracks.Any(t => t.Status == TrackStatus.Published && t.TrackGenres.Any(x => x.GenreId == genreId)))
                .ToItems().OrderByDescending(a => a.Plays).Take(4).ToList();
            ViewBag.Nav = "explore";
            ViewBag.Title = g.Name;
            return View(vm);
        }

        [HttpGet, Route("charts")]
        public ActionResult Charts()
        {
            var now = DateTime.UtcNow;
            var weekStart = now.AddDays(-7);
            var prevStart = now.AddDays(-14);
            var vm = new ChartsVm { From = weekStart, To = now };

            // Plays per track in this week and in the previous week, computed from listening_history.
            var thisWeek = Db.ListeningHistory.AsNoTracking()
                .Where(h => h.PlayedAt > weekStart && h.Track.Status == TrackStatus.Published)
                .GroupBy(h => h.TrackId).Select(g => new { TrackId = g.Key, Plays = g.Count() })
                .OrderByDescending(x => x.Plays).ThenBy(x => x.TrackId).ToList();
            var lastWeek = Db.ListeningHistory.AsNoTracking()
                .Where(h => h.PlayedAt > prevStart && h.PlayedAt <= weekStart && h.Track.Status == TrackStatus.Published)
                .GroupBy(h => h.TrackId).Select(g => new { TrackId = g.Key, Plays = g.Count() })
                .OrderByDescending(x => x.Plays).ThenBy(x => x.TrackId).ToList();
            var prevRank = new Dictionary<int, int>();
            for (var i = 0; i < lastWeek.Count; i++) prevRank[lastWeek[i].TrackId] = i + 1;

            var top = thisWeek.Take(20).ToList();
            var ids = top.Select(x => x.TrackId).ToList();
            var items = Db.Tracks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToItems().ToList().WithLikes(Db, CurrentUserId).ToDictionary(t => t.Id);
            for (var i = 0; i < top.Count; i++)
            {
                int r;
                vm.Tracks.Add(new ChartTrack
                {
                    Rank = i + 1, Plays = top[i].Plays, Track = items[top[i].TrackId],
                    PrevRank = prevRank.TryGetValue(top[i].TrackId, out r) ? r : (int?)null,
                });
            }

            // Artists: plays and unique listeners this week.
            var artistWeek = Db.ListeningHistory.AsNoTracking()
                .Where(h => h.PlayedAt > weekStart && h.Track.Status == TrackStatus.Published)
                .GroupBy(h => h.Track.ArtistId).Select(g => new { ArtistId = g.Key, Plays = g.Count(), Listeners = g.Select(x => x.UserId).Distinct().Count() })
                .OrderByDescending(x => x.Plays).ToList();
            var artistPrev = Db.ListeningHistory.AsNoTracking()
                .Where(h => h.PlayedAt > prevStart && h.PlayedAt <= weekStart && h.Track.Status == TrackStatus.Published)
                .GroupBy(h => h.Track.ArtistId).Select(g => new { ArtistId = g.Key, Plays = g.Count() })
                .OrderByDescending(x => x.Plays).ToList();
            var aPrev = new Dictionary<int, int>();
            for (var i = 0; i < artistPrev.Count; i++) aPrev[artistPrev[i].ArtistId] = i + 1;
            var topA = artistWeek.Take(10).ToList();
            var aIds = topA.Select(x => x.ArtistId).ToList();
            var aItems = ActiveArtists.Where(a => aIds.Contains(a.Id)).ToItems().ToList().ToDictionary(a => a.Id);
            var rank = 0;
            foreach (var x in topA.Where(x => aItems.ContainsKey(x.ArtistId)))
            {
                int r;
                rank++;
                vm.Artists.Add(new ChartArtist
                {
                    Rank = rank, Plays = x.Plays, Listeners = x.Listeners, Artist = aItems[x.ArtistId],
                    PrevRank = aPrev.TryGetValue(x.ArtistId, out r) ? r : (int?)null,
                });
            }

            vm.TotalPlays = thisWeek.Sum(x => x.Plays);
            vm.Listeners = Db.ListeningHistory.Where(h => h.PlayedAt > weekStart).Select(h => h.UserId).Distinct().Count();
            ViewBag.Nav = "charts";
            return View(vm);
        }
    }
}
