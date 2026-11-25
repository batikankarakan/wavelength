using System.Web.Mvc;
using System.Web.Routing;

namespace Wavelength.Web
{
    /// <summary>
    /// Custom routing (optional spec 2): short, readable URLs from the report's page list.
    /// Most routes are declared with [Route] attributes on the controllers (MapMvcAttributeRoutes);
    /// the conventional routes below add short links, constraints and the catch-all 404.
    /// </summary>
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");
            routes.LowercaseUrls = true;

            routes.MapMvcAttributeRoutes();
            RegisterConventionalRoutes(routes);
        }

        /// <summary>The hand-written routes (separate so unit tests can check them without IIS).</summary>
        public static void RegisterConventionalRoutes(RouteCollection routes)
        {
            // Share links: /s/{token}  (8-32 letters/digits)
            routes.MapRoute("ShareLink", "s/{token}",
                new { controller = "Share", action = "Open" },
                new { token = @"[A-Za-z0-9]{4,32}" });

            // Genre pages: /genres/{slug}  (lowercase slug)
            routes.MapRoute("Genre", "genres/{slug}",
                new { controller = "Discover", action = "Genre" },
                new { slug = @"[a-z0-9-]+" });

            // Public profiles: /users/{username}
            routes.MapRoute("UserProfile", "users/{username}",
                new { controller = "Users", action = "Details" },
                new { username = @"[A-Za-z0-9_.]{3,30}" });

            // Child actions rendered by the layout (@Html.Action) need a route to resolve; [ChildActionOnly]
            // keeps them from being opened directly.
            routes.MapRoute("Partials", "partials/{action}",
                new { controller = "Partials" },
                new { controller = "Partials" });

            // Anything else that matches no route -> friendly 404 page
            routes.MapRoute("NotFound", "{*url}", new { controller = "Error", action = "CatchAll" });
        }
    }
}
