using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Models;
using Wavelength.Web.Services;

namespace Wavelength.Web.Controllers
{
    /// <summary>Listener page 7: Profile Settings (profile, avatar, password, delete account).</summary>
    [Authorize]
    public class SettingsController : BaseController
    {
        private User CurrentUser()
        {
            var id = CurrentUserId.Value;
            return Db.Users.First(u => u.Id == id);
        }

        private static SettingsVm Build(User u, string section = "profile")
        {
            return new SettingsVm
            {
                Profile = new ProfileForm { DisplayName = u.DisplayName, Username = u.Username, Email = u.Email, Bio = u.Bio },
                AvatarUrl = u.AvatarUrl, Role = u.Role, CreatedAt = u.CreatedAt, Section = section,
            };
        }

        private void RemoveStateWithPrefix(string prefix)
        {
            foreach (var key in ModelState.Keys.Where(k => k.StartsWith(prefix)).ToList()) ModelState.Remove(key);
        }

        [HttpGet, Route("settings")]
        public ActionResult Index()
        {
            return View(Build(CurrentUser()));
        }

        [HttpPost, Route("settings/profile")]
        public ActionResult UpdateProfile([Bind(Prefix = "Profile")] ProfileForm f, HttpPostedFileBase avatar)
        {
            var u = CurrentUser();
            f = f ?? new ProfileForm();
            f.Email = (f.Email ?? "").Trim().ToLowerInvariant();
            f.Username = (f.Username ?? "").Trim();
            if (Db.Users.Any(x => x.Email == f.Email && x.Id != u.Id))
                ModelState.AddModelError("Profile.Email", "An account with this email already exists.");
            if (Db.Users.Any(x => x.Username == f.Username && x.Id != u.Id))
                ModelState.AddModelError("Profile.Username", "This username is taken.");
            var avatarError = FileStorage.ValidateImage(avatar);
            if (avatarError != null) ModelState.AddModelError("avatar", avatarError);

            // the password form is not posted here, so clear its (empty) validation state
            RemoveStateWithPrefix("Password.");
            if (!ModelState.IsValid)
            {
                var vm = Build(u);
                vm.Profile = f;
                return View("Index", vm);
            }

            u.DisplayName = f.DisplayName.Trim();
            u.Username = f.Username;
            u.Email = f.Email;
            u.Bio = string.IsNullOrWhiteSpace(f.Bio) ? null : f.Bio.Trim();
            if (FileStorage.HasFile(avatar))
            {
                Files.Delete(u.AvatarUrl);
                u.AvatarUrl = Files.Save(avatar, "avatars");
            }
            else if (f.RemoveAvatar)
            {
                Files.Delete(u.AvatarUrl);
                u.AvatarUrl = null;
            }
            Db.SaveChanges();
            Auth.SignIn(HttpContext, u);
            Flash("Profile saved.");
            return Redirect("/settings");
        }

        [HttpPost, Route("settings/password")]
        public ActionResult Password([Bind(Prefix = "Password")] PasswordForm f)
        {
            var u = CurrentUser();
            f = f ?? new PasswordForm();
            RemoveStateWithPrefix("Profile.");
            if (ModelState.IsValid && !Auth.Verify(u, f.CurrentPassword))
                ModelState.AddModelError("Password.CurrentPassword", "Current password is wrong.");
            if (!ModelState.IsValid) return View("Index", Build(u, "password"));

            u.PasswordHash = Auth.Hash(f.NewPassword);
            Db.SaveChanges();
            Auth.SignIn(HttpContext, u);
            Flash("Password changed.");
            return Redirect("/settings#password");
        }

        [HttpPost, Route("settings/delete")]
        public ActionResult Delete(string confirm)
        {
            var u = CurrentUser();
            if (u.Role == AppRoles.Moderator || u.Role == AppRoles.Admin)
            {
                Flash("Staff accounts cannot be deleted from here. Ask an admin to change your role first.", error: true);
                return Redirect("/settings#danger");
            }
            if (!string.Equals((confirm ?? "").Trim(), u.Username, StringComparison.Ordinal))
            {
                ModelState.AddModelError("confirm", "Type your username exactly to confirm.");
                return View("Index", Build(u, "danger"));
            }
            var avatar = u.AvatarUrl;
            var userId = u.Id;
            Cleanup.DeleteUser(userId);
            Files.Delete(avatar);
            Auth.SignOut(HttpContext);
            Flash("Your account and all your data were deleted. Goodbye!");
            return Redirect("/");
        }
    }
}
