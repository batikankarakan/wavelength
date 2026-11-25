using System.Web.Mvc;
using Wavelength.Web.Infrastructure;

namespace Wavelength.Web.Controllers
{
    /// <summary>Public page 18: friendly 403 / 404 pages (and a generic error page).</summary>
    public class ErrorController : BaseController
    {
        [Route("403")]
        public ActionResult Forbidden() { return ForbiddenView(); }

        [Route("404")]
        public ActionResult NotFound() { return NotFoundView(); }

        /// <summary>Target of the catch-all route in RouteConfig (URLs that match nothing).</summary>
        public ActionResult CatchAll() { return NotFoundView(); }

        [Route("error")]
        public ActionResult ServerError()
        {
            Response.StatusCode = 500;
            Response.TrySkipIisCustomErrors = true;
            return View("~/Views/Shared/Error.cshtml", 500);
        }
    }
}
