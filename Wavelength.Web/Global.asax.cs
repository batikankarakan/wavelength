using System;
using System.Data.Entity;
using System.Web;
using System.Web.Helpers;
using System.Web.Mvc;
using System.Web.Routing;
using Wavelength.Web.Data;

namespace Wavelength.Web
{
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            // Creates the LocalDB database with the 19 tables and demo data on first run.
            Database.SetInitializer(new WavelengthInitializer());
            using (var db = new AppDbContext()) db.Database.Initialize(false);

            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            ModelBinders.Binders.Add(typeof(string), new TrimmingModelBinder());

            AntiForgeryConfig.UniqueClaimTypeIdentifier = System.Security.Claims.ClaimTypes.NameIdentifier;
            MvcHandler.DisableMvcResponseHeader = true;
        }

        /// <summary>Unhandled errors show the friendly error page (in Release / for remote users).</summary>
        protected void Application_Error(object sender, EventArgs e)
        {
            var ex = Server.GetLastError();
            var http = ex as HttpException;
            if (http != null && http.GetHttpCode() == 404)
            {
                Server.ClearError();
                Response.Clear();
                Response.TrySkipIisCustomErrors = true;
                Server.TransferRequest("~/404");
            }
        }
    }

    /// <summary>Trims whitespace from every posted string so "  name " is stored as "name".</summary>
    public class TrimmingModelBinder : DefaultModelBinder
    {
        public override object BindModel(ControllerContext controllerContext, ModelBindingContext bindingContext)
        {
            var value = base.BindModel(controllerContext, bindingContext) as string;
            // passwords are kept exactly as typed
            if (bindingContext.ModelName.IndexOf("Password", StringComparison.OrdinalIgnoreCase) >= 0) return value;
            return string.IsNullOrWhiteSpace(value) ? (value == null ? null : value.Trim()) : value.Trim();
        }
    }
}
