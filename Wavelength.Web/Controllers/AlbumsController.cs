using System.Linq;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Models;
using Wavelength.Web.Services;

namespace Wavelength.Web.Controllers
{
    /// <summary>Public page 11: Album page, with save to library.</summary>
    public class AlbumsController : BaseController
    {
        [HttpGet, Route("albums/{id:int}")]
        public ActionResult Details(int id)
        {
            var uid = CurrentUserId;
            var album = Db.Albums.AsNoTracking().Where(a => a.Id == id && a.Artist.User.IsActive).ToItems().FirstOrDefault();
            if (album == null) return NotFoundView();
            var artistId = album.ArtistId;
            var vm = new AlbumPageVm
            {
                Album = album,
                ArtistVerified = Db.ArtistProfiles.Where(a => a.Id == artistId).Select(a => a.Verified).FirstOrDefault(),
                Tracks = Db.Tracks.AsNoTracking().Published().Where(t => t.AlbumId == id).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).ToItems().ToList().WithLikes(Db, uid),
                Saved = uid != null && Db.SavedAlbums.Any(s => s.UserId == uid && s.AlbumId == id),
                Saves = Db.SavedAlbums.Count(s => s.AlbumId == id),
                More = Db.Albums.AsNoTracking().Where(a => a.ArtistId == artistId && a.Id != id).ToItems().Where(a => a.TrackCount > 0).OrderByDescending(a => a.ReleaseDate).Take(4).ToList(),
            };
            if (vm.Tracks.Count == 0 && !User.IsStaff()) return NotFoundView();
            ViewBag.Title = album.Title + " by " + album.Artist;
            ViewBag.Nav = "explore";
            return View(vm);
        }

        [Authorize]
        [HttpPost, Route("albums/{id:int}/save")]
        public ActionResult Save(int id, string returnUrl)
        {
            var uid = CurrentUserId.Value;
            if (!Db.Albums.Any(a => a.Id == id)) return NotFoundView();
            var s = Db.SavedAlbums.Find(uid, id);
            if (s == null) Db.SavedAlbums.Add(new SavedAlbum { UserId = uid, AlbumId = id });
            else Db.SavedAlbums.Remove(s);
            Db.SaveChanges();
            Flash(s == null ? "Album saved to your library." : "Album removed from your library.");
            return SafeRedirect(returnUrl, "/albums/" + id);
        }
    }
}
