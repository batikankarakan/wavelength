using System.Text;
using System.Web.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Wavelength.Web.Data;
using Wavelength.Web.Services;

namespace Wavelength.Web.Infrastructure
{
    /// <summary>
    /// Base class for all controllers: one database context per request, the shared services,
    /// flash messages, friendly 404/403 pages and camelCase JSON for the Vue/jQuery code.
    /// </summary>
    public abstract class BaseController : Controller
    {
        private AppDbContext _db;
        protected AppDbContext Db { get { return _db ?? (_db = new AppDbContext()); } }

        protected AuthService Auth { get { return new AuthService(); } }
        protected NotificationService Notes { get { return new NotificationService(Db); } }
        protected AuditService Audit { get { return new AuditService(Db); } }
        protected CleanupService Cleanup { get { return new CleanupService(Db); } }
        protected FileStorage Files { get { return new FileStorage(); } }

        /// <summary>Id of the signed-in user, or null for visitors.</summary>
        protected int? CurrentUserId { get { return User.UserId(); } }

        /// <summary>Shows a toast on the next page (TempData survives one redirect).</summary>
        protected void Flash(string message, bool error = false)
        {
            TempData["Flash"] = message;
            TempData["FlashKind"] = error ? "error" : "ok";
        }

        /// <summary>The friendly 404 page with a real 404 status code.</summary>
        protected ActionResult NotFoundView()
        {
            Response.StatusCode = 404;
            Response.TrySkipIisCustomErrors = true;
            return View("~/Views/Shared/Error.cshtml", 404);
        }

        /// <summary>The friendly 403 page with a real 403 status code.</summary>
        protected ActionResult ForbiddenView()
        {
            Response.StatusCode = 403;
            Response.TrySkipIisCustomErrors = true;
            return View("~/Views/Shared/Error.cshtml", 403);
        }

        /// <summary>Redirects to a local return URL, or to the fallback (prevents open redirects).</summary>
        protected ActionResult SafeRedirect(string returnUrl, string fallback = "/")
        {
            return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : Redirect(fallback);
        }

        private static readonly JsonSerializerSettings CamelCase = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };

        /// <summary>JSON response with camelCase names (MVC 5's Json() keeps PascalCase).</summary>
        protected ActionResult CamelJson(object data, int status = 200)
        {
            Response.StatusCode = status;
            Response.TrySkipIisCustomErrors = true;
            return new ContentResult { Content = JsonConvert.SerializeObject(data, CamelCase), ContentType = "application/json", ContentEncoding = Encoding.UTF8 };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _db != null) _db.Dispose();
            base.Dispose(disposing);
        }
    }
}
