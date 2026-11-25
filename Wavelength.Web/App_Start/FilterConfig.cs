using System.Web.Mvc;
using Wavelength.Web.Infrastructure;

namespace Wavelength.Web
{
    public class FilterConfig
    {
        public static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute { View = "Error" });
            // Every POST must carry an antiforgery token (form field or X-CSRF-TOKEN header for AJAX).
            filters.Add(new ValidateAntiForgeryOnPostAttribute());
            filters.Add(new AntiForgeryCookieFilter());
        }
    }
}
