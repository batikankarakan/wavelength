using System;
using System.Web.Helpers;
using System.Web.Mvc;

namespace Wavelength.Web.Infrastructure
{
    /// <summary>
    /// Global filter: every POST must carry a valid antiforgery token, either as the hidden form field written by
    /// @Html.AntiForgeryToken() or, for AJAX calls, in the X-CSRF-TOKEN header (read from the layout's token).
    /// </summary>
    public class ValidateAntiForgeryOnPostAttribute : FilterAttribute, IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationContext filterContext)
        {
            var request = filterContext.HttpContext.Request;
            if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)) return;
            if (filterContext.IsChildAction) return;
            try
            {
                var header = request.Headers["X-CSRF-TOKEN"];
                if (!string.IsNullOrEmpty(header))
                {
                    var cookie = request.Cookies[AntiForgeryConfig.CookieName];
                    AntiForgery.Validate(cookie == null ? null : cookie.Value, header);
                }
                else
                {
                    AntiForgery.Validate();
                }
            }
            catch (HttpAntiForgeryException ex)
            {
                System.Diagnostics.Trace.TraceWarning("Antiforgery check failed: " + ex.Message);
                filterContext.Result = new HttpStatusCodeResult(400, "Invalid or missing antiforgery token");
            }
        }
    }

    /// <summary>
    /// Makes every @Html.AntiForgeryToken() call in one request share the same cookie token.
    /// Without this, a first-time visitor's page (layout token + form token) would get two different cookie
    /// tokens and only the last one would be kept, so the form's token would not match on submit.
    /// </summary>
    public class AntiForgeryCookieFilter : IResultFilter
    {
        public void OnResultExecuting(ResultExecutingContext filterContext)
        {
            if (filterContext.IsChildAction || !(filterContext.Result is ViewResultBase)) return;
            var http = filterContext.HttpContext;
            var name = AntiForgeryConfig.CookieName;
            var existing = http.Request.Cookies[name];
            string newCookie, formToken;
            AntiForgery.GetTokens(existing == null ? null : existing.Value, out newCookie, out formToken);
            if (newCookie == null) return; // the request already has a valid cookie token
            var cookie = new System.Web.HttpCookie(name, newCookie) { HttpOnly = true, Secure = AntiForgeryConfig.RequireSsl };
            http.Response.Cookies.Set(cookie);
            http.Request.Cookies.Set(new System.Web.HttpCookie(name, newCookie)); // later helper calls reuse it
        }

        public void OnResultExecuted(ResultExecutedContext filterContext) { }
    }
}
