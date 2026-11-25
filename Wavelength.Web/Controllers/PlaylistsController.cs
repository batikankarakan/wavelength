using System.Linq;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Models;
using Wavelength.Web.Services;

namespace Wavelength.Web.Controllers
{
    /// <summary>Public page 14: Playlist details (private playlists only for their owner).</summary>
    public class PlaylistsController : BaseController
    {
        [HttpGet, Route("playlists/{id:int}")]
        public ActionResult Details(int id)
        {
            var uid = CurrentUserId;
            var p = Db.Playlists.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.OwnerId, x.IsPublic, x.CreatedAt, x.Owner.AvatarUrl, x.Owner.IsActive }).FirstOrDefault();
            if (p == null || (!p.IsPublic && p.OwnerId != uid) || (!p.IsActive && p.OwnerId != uid)) return NotFoundView();
            var item = Db.Playlists.AsNoTracking().Where(x => x.Id == id).ToItems().First();
            var trackIds = Db.PlaylistTracks.Where(pt => pt.PlaylistId == id).OrderBy(pt => pt.Position).Select(pt => pt.TrackId).ToList();
            var tracks = Db.Tracks.AsNoTracking().Published().Where(t => trackIds.Contains(t.Id)).ToItems().ToList().ToDictionary(t => t.Id);
            var vm = new PlaylistPageVm
            {
                Playlist = item, OwnerId = p.OwnerId, OwnerAvatar = p.AvatarUrl, CreatedAt = p.CreatedAt, IsOwner = p.OwnerId == uid,
                Tracks = trackIds.Where(tracks.ContainsKey).Select(i => tracks[i]).ToList(),
                Saved = uid != null && Db.SavedPlaylists.Any(s => s.UserId == uid && s.PlaylistId == id),
                Saves = Db.SavedPlaylists.Count(s => s.PlaylistId == id),
            };
            vm.Tracks.WithLikes(Db, uid);
            ViewBag.Title = item.Title;
            ViewBag.Nav = vm.IsOwner ? "library" : "explore";
            return View(vm);
        }

        [Authorize]
        [HttpPost, Route("playlists/{id:int}/save")]
        public ActionResult Save(int id, string returnUrl)
        {
            var uid = CurrentUserId.Value;
            var p = Db.Playlists.AsNoTracking().FirstOrDefault(x => x.Id == id);
            if (p == null || (!p.IsPublic && p.OwnerId != uid)) return NotFoundView();
            var back = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/playlists/" + id;
            if (p.OwnerId == uid)
            {
                Flash("This is your own playlist, it's already in your library.", error: true);
                return Redirect(back);
            }
            var s = Db.SavedPlaylists.Find(uid, id);
            if (s == null) Db.SavedPlaylists.Add(new SavedPlaylist { UserId = uid, PlaylistId = id });
            else Db.SavedPlaylists.Remove(s);
            Db.SaveChanges();
            Flash(s == null ? "Playlist saved to your library." : "Playlist removed from your library.");
            return Redirect(back);
        }
    }
}
