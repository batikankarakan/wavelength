using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Web.Mvc;

using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Services;

namespace Wavelength.Web.Controllers
{
    public class RegisterForm
    {
        [Required(ErrorMessage = "Please tell us your name."), StringLength(60, MinimumLength = 2, ErrorMessage = "Name must be 2-60 characters.")]
        [Display(Name = "Name")]
        public string DisplayName { get; set; }

        [Required(ErrorMessage = "Pick a username."), StringLength(30, MinimumLength = 3, ErrorMessage = "Username must be 3-30 characters.")]
        [RegularExpression("^[a-zA-Z0-9_.]+$", ErrorMessage = "Use letters, numbers, dots and underscores only.")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Enter your email."), EmailAddress(ErrorMessage = "Enter a valid email address."), StringLength(120)]
        public string Email { get; set; }

        [Required(ErrorMessage = "Choose a password."), StringLength(100, MinimumLength = 8, ErrorMessage = "Password needs at least 8 characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        [Required(ErrorMessage = "Repeat the password."), System.ComponentModel.DataAnnotations.Compare("Password", ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password), Display(Name = "Repeat")]
        public string ConfirmPassword { get; set; }

        public bool AsArtist { get; set; }

        [StringLength(80, ErrorMessage = "Stage name can be at most 80 characters."), Display(Name = "Stage name")]
        public string StageName { get; set; }

        /// <summary>Checked on the server in Register (a [Range] on a checkbox confuses client validation).</summary>
        public bool AcceptTerms { get; set; }
    }

    public class LoginForm
    {
        public LoginForm() { Remember = true; }

        [Required(ErrorMessage = "Enter your email."), EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Enter your password."), DataType(DataType.Password)]
        public string Password { get; set; }

        public bool Remember { get; set; }
        public string ReturnUrl { get; set; }
    }

    public class ForgotForm
    {
        [Required(ErrorMessage = "Enter your email."), EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; }
    }

    public class ResetForm
    {
        public string Token { get; set; }

        [Required(ErrorMessage = "Choose a password."), StringLength(100, MinimumLength = 8, ErrorMessage = "Password needs at least 8 characters.")]
        [DataType(DataType.Password), Display(Name = "New password")]
        public string Password { get; set; }

        [Required(ErrorMessage = "Repeat the password."), System.ComponentModel.DataAnnotations.Compare("Password", ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password), Display(Name = "Repeat")]
        public string ConfirmPassword { get; set; }
    }

    /// <summary>Public pages 4-6: Sign up, Log in, Forgot / Reset password.</summary>
    public class AccountController : BaseController
    {
        // ---------- Sign up ----------
        [HttpGet, Route("register")]
        public ActionResult Register()
        {
            if (Request.IsAuthenticated) return Redirect("/");
            return View(new RegisterForm());
        }

        [HttpPost, Route("register")]
        public ActionResult Register(RegisterForm f)
        {
            f.Email = (f.Email ?? "").Trim().ToLowerInvariant();
            f.Username = (f.Username ?? "").Trim();
            if (!f.AcceptTerms)
                ModelState.AddModelError("AcceptTerms", "Please accept the terms.");
            if (f.AsArtist && string.IsNullOrWhiteSpace(f.StageName))
                ModelState.AddModelError("StageName", "Artists need a stage name.");
            if (Db.Users.Any(u => u.Email == f.Email))
                ModelState.AddModelError("Email", "An account with this email already exists.");
            if (Db.Users.Any(u => u.Username == f.Username))
                ModelState.AddModelError("Username", "This username is taken.");
            if (!ModelState.IsValid) return View(f);

            var user = new User
            {
                Username = f.Username, Email = f.Email, DisplayName = f.DisplayName.Trim(),
                Role = f.AsArtist ? AppRoles.Artist : AppRoles.Listener,
                PasswordHash = Auth.Hash(f.Password),
            };
            Db.Users.Add(user);
            if (f.AsArtist) Db.ArtistProfiles.Add(new ArtistProfile { User = user, StageName = f.StageName.Trim() });
            Db.SaveChanges();
            Notes.Add(user.Id, "welcome", new { });
            Db.SaveChanges();

            Auth.SignIn(HttpContext, user);
            Flash("Welcome to Wavelength, " + user.DisplayName + "!");
            return Redirect(f.AsArtist ? "/studio" : "/explore");
        }

        // ---------- Log in / out ----------
        [HttpGet, Route("login")]
        public ActionResult Login(string returnUrl)
        {
            if (Request.IsAuthenticated) return SafeRedirect(returnUrl);
            return View(new LoginForm { ReturnUrl = returnUrl });
        }

        [HttpPost, Route("login")]
        public ActionResult Login(LoginForm f)
        {
            if (!ModelState.IsValid) return View(f);
            var email = f.Email.Trim().ToLowerInvariant();
            var user = Db.Users.FirstOrDefault(u => u.Email == email);
            if (user == null || !Auth.Verify(user, f.Password))
            {
                ModelState.AddModelError("", "Wrong email or password.");
                return View(f);
            }
            if (!user.IsActive)
            {
                ModelState.AddModelError("", "This account has been suspended. Contact us if you think this is a mistake.");
                return View(f);
            }
            Auth.SignIn(HttpContext, user, f.Remember);
            Flash("Welcome back, " + user.DisplayName + ".");
            return SafeRedirect(f.ReturnUrl);
        }

        [HttpPost, Route("logout")]
        public ActionResult Logout()
        {
            Auth.SignOut(HttpContext);
            Flash("You are logged out. See you soon.");
            return Redirect("/");
        }

        // ---------- Forgot / reset password ----------
        // The token is a signed, encrypted value (MachineKey) holding the user id, an expiry time and the user's
        // stamp, so it expires after one hour and stops working as soon as the password changes. No extra table.
        private const string ResetPurpose = "Wavelength.PasswordReset";

        private static string MakeToken(User u)
        {
            var payload = u.Id + "|" + DateTime.UtcNow.AddHours(1).Ticks + "|" + AuthService.Stamp(u);
            var bytes = System.Web.Security.MachineKey.Protect(Encoding.UTF8.GetBytes(payload), ResetPurpose);
            return HttpServerUtilityBase64(bytes);
        }

        private static string HttpServerUtilityBase64(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] FromBase64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }

        private User UserFromToken(string token)
        {
            try
            {
                var parts = Encoding.UTF8.GetString(System.Web.Security.MachineKey.Unprotect(FromBase64Url(token), ResetPurpose)).Split('|');
                if (new DateTime(long.Parse(parts[1]), DateTimeKind.Utc) < DateTime.UtcNow) return null;
                var id = int.Parse(parts[0]);
                var user = Db.Users.Find(id);
                return user != null && user.IsActive && AuthService.Stamp(user) == parts[2] ? user : null;
            }
            catch { return null; }
        }

        [HttpGet, Route("forgot-password")]
        public ActionResult Forgot() { return View(new ForgotForm()); }

        [HttpPost, Route("forgot-password")]
        public ActionResult Forgot(ForgotForm f)
        {
            if (!ModelState.IsValid) return View(f);
            var email = f.Email.Trim().ToLowerInvariant();
            var user = Db.Users.FirstOrDefault(u => u.Email == email && u.IsActive);
            ViewBag.Sent = true;
            ViewBag.Email = email;
            if (user != null)
            {
                var link = Url.Action("Reset", "Account", new { token = MakeToken(user) }, Request.Url.Scheme);
                // There is no mail server in this project: the link is written to the trace log and,
                // when Wavelength:ShowResetLink is true in Web.config, shown on the page.
                System.Diagnostics.Trace.TraceInformation("Password reset link for " + email + ": " + link);
                if (System.Configuration.ConfigurationManager.AppSettings["Wavelength:ShowResetLink"] == "true")
                    ViewBag.DevLink = link;
            }
            return View(f);
        }

        [HttpGet, Route("reset-password")]
        public ActionResult Reset(string token)
        {
            ViewBag.Invalid = UserFromToken(token ?? "") == null;
            return View(new ResetForm { Token = token ?? "" });
        }

        [HttpPost, Route("reset-password")]
        public ActionResult Reset(ResetForm f)
        {
            var user = UserFromToken(f.Token ?? "");
            if (user == null) { ViewBag.Invalid = true; return View(f); }
            if (!ModelState.IsValid) { ViewBag.Invalid = false; return View(f); }
            user.PasswordHash = Auth.Hash(f.Password);
            Db.SaveChanges();
            Auth.SignIn(HttpContext, user);
            Flash("Password updated. You are logged in.");
            return Redirect("/");
        }
    }
}
