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
    /// <summary>Public page 10: Artist profile.</summary>
    public class ArtistsController : BaseController
    {
        [HttpGet, Route("artists/{id:int}")]
        public ActionResult Details(int id)
        {
            var uid = CurrentUserId;
            var a = Db.ArtistProfiles.AsNoTracking().Include(x => x.User).FirstOrDefault(x => x.Id == id);
            if (a == null || (!a.User.IsActive && !User.IsStaff())) return NotFoundView();
            var since = DateTime.UtcNow.AddDays(-30);
            var artistUserId = a.UserId;
            var vm = new ArtistPageVm
            {
                Artist = a, Username = a.User.Username, Bio = a.User.Bio, AvatarUrl = a.User.AvatarUrl, IsSelf = uid == a.UserId,
                Followers = Db.Follows.Count(f => f.FollowedId == artistUserId),
                TotalPlays = Db.Tracks.Where(t => t.ArtistId == id && t.Status == TrackStatus.Published).Sum(t => (long?)t.PlaysCount) ?? 0,
                TrackCount = Db.Tracks.Count(t => t.ArtistId == id && t.Status == TrackStatus.Published),
                MonthlyListeners = Db.ListeningHistory.Where(h => h.Track.ArtistId == id && h.PlayedAt > since).Select(h => h.UserId).Distinct().Count(),
                Following = uid != null && Db.Follows.Any(f => f.FollowerId == uid && f.FollowedId == artistUserId),
                TopTracks = Db.Tracks.AsNoTracking().Published().Where(t => t.ArtistId == id).OrderByDescending(t => t.PlaysCount).Take(10).ToItems().ToList().WithLikes(Db, uid),
                Albums = Db.Albums.AsNoTracking().Where(al => al.ArtistId == id).ToItems().Where(al => al.TrackCount > 0).OrderByDescending(al => al.ReleaseDate).ToList(),
            };
            // "Fans also like": artists sharing genres with this one.
            var genreIds = Db.TrackGenres.Where(g => g.Track.ArtistId == id && g.Track.Status == TrackStatus.Published).Select(g => g.GenreId).Distinct().ToList();
            vm.Similar = Db.ArtistProfiles.AsNoTracking()
                .Where(x => x.Id != id && x.User.IsActive && x.Tracks.Any(t => t.Status == TrackStatus.Published && t.TrackGenres.Any(g => genreIds.Contains(g.GenreId))))
                .ToItems().OrderByDescending(x => x.Plays).Take(4).ToList();
            ViewBag.Title = a.StageName;
            ViewBag.Nav = "explore";
            return View(vm);
        }
    }
}
