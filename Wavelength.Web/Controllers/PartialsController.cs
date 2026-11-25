using System.Linq;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Services;

namespace Wavelength.Web.Controllers
{
    public class NavVm
    {
        public string Active { get; set; }
        public bool SignedIn { get; set; }
        public string DisplayName { get; set; }
        public string Username { get; set; }
        public string AvatarUrl { get; set; }
        public string Role { get; set; }
        public int Unread { get; set; }
    }

    /// <summary>Child actions rendered by the layout (@Html.Action).</summary>
    public class PartialsController : BaseController
    {
        [ChildActionOnly]
        public ActionResult Nav(string active)
        {
            var vm = new NavVm { Active = active ?? "" };
            var id = CurrentUserId;
            if (id != null)
            {
                var u = Db.Users.AsNoTracking().Where(x => x.Id == id)
                    .Select(x => new { x.DisplayName, x.Username, x.AvatarUrl, x.Role }).FirstOrDefault();
                if (u != null)
                {
                    vm.SignedIn = true;
                    vm.DisplayName = u.DisplayName;
                    vm.Username = u.Username;
                    vm.AvatarUrl = u.AvatarUrl;
                    vm.Role = u.Role;
                    vm.Unread = Db.Notifications.Count(n => n.UserId == id && !n.IsRead);
                }
            }
            return PartialView("~/Views/Partials/_Nav.cshtml", vm);
        }
    }
}
