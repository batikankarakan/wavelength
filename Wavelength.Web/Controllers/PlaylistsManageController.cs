using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Models;
using Wavelength.Web.Services;

namespace Wavelength.Web.Controllers
{
    /// <summary>Listener page 3: Create / Edit Playlist, plus delete and "add to playlist".</summary>
    [Authorize]
    public class PlaylistsManageController : BaseController
    {
        private int Me { get { return CurrentUserId.Value; } }

        private List<EditorTrack> EditorTracks(IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            var items = Db.Tracks.AsNoTracking().Published().Where(t => list.Contains(t.Id)).ToItems().ToList();
            return items.OrderBy(i => list.IndexOf(i.Id))
                .Select(i => new EditorTrack { Id = i.Id, Title = i.Title, Artist = i.Artist, CoverUrl = i.CoverUrl, Color = i.Color, DurationSec = i.DurationSec })
                .ToList();
        }

        [HttpGet, Route("playlists/new")]
        public ActionResult New(int? trackId)
        {
            var form = new PlaylistForm();
            if (trackId != null) form.TrackIds.Add(trackId.Value);
            return View("Edit", new PlaylistEditVm { Form = form, Tracks = EditorTracks(form.TrackIds) });
        }

        [HttpGet, Route("playlists/{id:int}/edit")]
        public ActionResult Edit(int id)
        {
            var p = Db.Playlists.AsNoTracking().Include(x => x.PlaylistTracks).FirstOrDefault(x => x.Id == id);
            if (p == null) return NotFoundView();
            if (p.OwnerId != Me) return ForbiddenView();
            var form = new PlaylistForm
            {
                Id = p.Id, Title = p.Title, Description = p.Description, IsPublic = p.IsPublic, CoverUrl = p.CoverUrl,
                TrackIds = p.PlaylistTracks.OrderBy(t => t.Position).Select(t => t.TrackId).ToList(),
            };
            return View("Edit", new PlaylistEditVm { Form = form, Tracks = EditorTracks(form.TrackIds) });
        }

        [HttpPost, Route("playlists/new")]
        public ActionResult Create([Bind(Prefix = "Form")] PlaylistForm form, HttpPostedFileBase cover)
        {
            return Save(null, form, cover);
        }

        [HttpPost, Route("playlists/{id:int}/edit")]
        public ActionResult Update(int id, [Bind(Prefix = "Form")] PlaylistForm form, HttpPostedFileBase cover)
        {
            return Save(id, form, cover);
        }

        private ActionResult Save(int? id, PlaylistForm form, HttpPostedFileBase cover)
        {
            form = form ?? new PlaylistForm();
            Playlist p = null;
            if (id != null)
            {
                p = Db.Playlists.Include(x => x.PlaylistTracks).FirstOrDefault(x => x.Id == id);
                if (p == null) return NotFoundView();
                if (p.OwnerId != Me) return ForbiddenView();
            }
            var coverError = FileStorage.ValidateImage(cover);
            if (coverError != null) ModelState.AddModelError("cover", coverError);

            // only published tracks can be added
            var requested = (form.TrackIds ?? new List<int>()).Distinct().ToList();
            var valid = Db.Tracks.Published().Where(t => requested.Contains(t.Id)).Select(t => t.Id).ToList();
            var ordered = requested.Where(valid.Contains).ToList();

            if (!ModelState.IsValid)
            {
                form.Id = id;
                form.CoverUrl = p != null ? p.CoverUrl : null;
                form.TrackIds = ordered;
                return View("Edit", new PlaylistEditVm { Form = form, Tracks = EditorTracks(ordered) });
            }

            if (p == null)
            {
                p = new Playlist { OwnerId = Me };
                Db.Playlists.Add(p);
            }
            p.Title = form.Title.Trim();
            p.Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim();
            p.IsPublic = form.IsPublic;

            if (FileStorage.HasFile(cover))
            {
                Files.Delete(p.CoverUrl);
                p.CoverUrl = Files.Save(cover, "covers");
            }
            else if (form.RemoveCover)
            {
                Files.Delete(p.CoverUrl);
                p.CoverUrl = null;
            }

            // rewrite positions 1..n, keeping the original added_at for tracks that stay
            var existing = p.PlaylistTracks.ToDictionary(x => x.TrackId, x => x.AddedAt);
            Db.PlaylistTracks.RemoveRange(p.PlaylistTracks.ToList());
            Db.SaveChanges();
            var pos = 1;
            foreach (var tid in ordered)
            {
                DateTime added;
                if (!existing.TryGetValue(tid, out added)) added = DateTime.UtcNow;
                Db.PlaylistTracks.Add(new PlaylistTrack { PlaylistId = p.Id, TrackId = tid, Position = pos++, AddedAt = added });
            }
            Db.SaveChanges();

            Flash(id == null ? "Playlist \"" + p.Title + "\" created." : "Playlist saved.");
            return Redirect("/playlists/" + p.Id);
        }

        [HttpPost, Route("playlists/{id:int}/delete")]
        public ActionResult Delete(int id)
        {
            var p = Db.Playlists.AsNoTracking().FirstOrDefault(x => x.Id == id);
            if (p == null) return NotFoundView();
            if (p.OwnerId != Me) return ForbiddenView();
            Cleanup.DeletePlaylist(id);
            Files.Delete(p.CoverUrl);
            Flash("Playlist \"" + p.Title + "\" deleted.");
            return Redirect("/library/playlists");
        }

        [HttpPost, Route("playlists/{playlistId:int}/add-track")]
        public ActionResult AddTrack(int playlistId, int trackId, string returnUrl)
        {
            var back = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/tracks/" + trackId;
            var p = Db.Playlists.FirstOrDefault(x => x.Id == playlistId);
            if (p == null) return NotFoundView();
            if (p.OwnerId != Me) return ForbiddenView();
            if (!Db.Tracks.Published().Any(t => t.Id == trackId))
            {
                Flash("That track is not available.", error: true);
                return Redirect(back);
            }
            if (Db.PlaylistTracks.Any(x => x.PlaylistId == playlistId && x.TrackId == trackId))
            {
                Flash("Already in \"" + p.Title + "\".");
                return Redirect(back);
            }
            var max = Db.PlaylistTracks.Where(x => x.PlaylistId == playlistId).Max(x => (int?)x.Position) ?? 0;
            Db.PlaylistTracks.Add(new PlaylistTrack { PlaylistId = playlistId, TrackId = trackId, Position = max + 1 });
            Db.SaveChanges();
            Flash("Added to \"" + p.Title + "\".");
            return Redirect(back);
        }
    }
}
