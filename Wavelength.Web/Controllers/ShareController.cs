using System.Linq;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;

namespace Wavelength.Web.Controllers
{
    /// <summary>Public page 15: resolves a share link, counts the click and opens the track.</summary>
    public class ShareController : BaseController
    {
        /// <summary>/s/{token} — routed by the "ShareLink" route in RouteConfig.</summary>
        [HttpGet]
        public ActionResult Open(string token)
        {
            var link = Db.ShareLinks.AsNoTracking().Where(s => s.Token == token)
                .Select(s => new { s.Id, s.TrackId, s.Track.Status }).FirstOrDefault();
            if (link == null || link.Status != TrackStatus.Published) return NotFoundView();
            Db.Database.ExecuteSqlCommand("UPDATE share_links SET click_count = click_count + 1 WHERE id = @p0", link.Id);
            return Redirect("/tracks/" + link.TrackId + "?from=share");
        }
    }
}
