using System.Linq;
using System.Web.Mvc;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Models;
using Wavelength.Web.Services;

namespace Wavelength.Web.Controllers
{
    /// <summary>Public page 16: public user profile.</summary>
    public class UsersController : BaseController
    {
        /// <summary>/users/{username} — routed by the "UserProfile" route in RouteConfig.</summary>
        [HttpGet]
        public ActionResult Details(string username)
        {
            var uid = CurrentUserId;
            var u = Db.Users.AsNoTracking().FirstOrDefault(x => x.Username == username);
            if (u == null || (!u.IsActive && !User.IsStaff())) return NotFoundView();
            var userId = u.Id;
            var follows = Db.Follows;
            var vm = new UserPageVm
            {
                Profile = u, IsSelf = uid == u.Id,
                ArtistId = Db.ArtistProfiles.Where(a => a.UserId == userId).Select(a => (int?)a.Id).FirstOrDefault(),
                Followers = Db.Follows.Count(f => f.FollowedId == userId),
                Following = Db.Follows.Count(f => f.FollowerId == userId),
                LikedCount = Db.TrackLikes.Count(l => l.UserId == userId),
                IsFollowing = uid != null && Db.Follows.Any(f => f.FollowerId == uid && f.FollowedId == userId),
                Playlists = Db.Playlists.AsNoTracking().Where(p => p.OwnerId == userId && p.IsPublic).OrderByDescending(p => p.CreatedAt).ToItems().ToList(),
                PrivatePlaylists = Db.Playlists.Count(p => p.OwnerId == userId && !p.IsPublic),
                FollowsArtists = Db.ArtistProfiles.AsNoTracking().Where(a => a.User.IsActive && follows.Any(f => f.FollowerId == userId && f.FollowedId == a.UserId))
                    .ToItems().OrderByDescending(a => a.Plays).Take(8).ToList(),
            };
            ViewBag.Title = u.DisplayName + " (@" + u.Username + ")";
            return View(vm);
        }
    }
}
