using System;
using Microsoft.AspNet.Identity;
using Microsoft.Owin;
using Microsoft.Owin.Security.Cookies;
using Owin;
using Wavelength.Web.Services;

[assembly: OwinStartup(typeof(Wavelength.Web.Startup))]

namespace Wavelength.Web
{
    /// <summary>OWIN cookie authentication (sign in / sign up, roles as claims).</summary>
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationType = DefaultAuthenticationTypes.ApplicationCookie,
                CookieName = "wavelength.auth",
                CookieHttpOnly = true,
                LoginPath = new PathString("/login"),
                LogoutPath = new PathString("/logout"),
                ExpireTimeSpan = TimeSpan.FromDays(14),
                SlidingExpiration = true,
                Provider = new CookieAuthenticationProvider
                {
                    // Re-check the account on every request: banned users are signed out and
                    // role changes take effect immediately.
                    OnValidateIdentity = AuthService.ValidateIdentity,
                    // [Authorize(Roles = ...)] answers 401 for every failure. Signed-in users without the role
                    // go to the friendly /403 page, visitors to /login, and AJAX/API calls just get the status code.
                    OnApplyRedirect = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments(new PathString("/api"))) return;
                        var user = ctx.Request.User;
                        if (user != null && user.Identity != null && user.Identity.IsAuthenticated)
                            ctx.Response.Redirect("/403");
                        else
                            ctx.Response.Redirect(ctx.RedirectUri);
                    },
                },
            });
        }
    }
}
