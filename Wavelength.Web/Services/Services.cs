using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Hosting;
using Microsoft.AspNet.Identity;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Cookies;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Wavelength.Web.Data;

namespace Wavelength.Web.Services
{
    public static class PrincipalExtensions
    {
        public static int? UserId(this IPrincipal p)
        {
            var c = p as ClaimsPrincipal;
            var v = c == null ? null : c.FindFirst(ClaimTypes.NameIdentifier);
            int id;
            return v != null && int.TryParse(v.Value, out id) ? id : (int?)null;
        }

        public static string Role(this IPrincipal p)
        {
            var c = p as ClaimsPrincipal;
            var v = c == null ? null : c.FindFirst(ClaimTypes.Role);
            return v == null ? null : v.Value;
        }

        public static string DisplayName(this IPrincipal p)
        {
            var c = p as ClaimsPrincipal;
            var v = c == null ? null : c.FindFirst("display_name");
            return v == null ? null : v.Value;
        }

        public static bool IsSignedIn(this IPrincipal p) { return p != null && p.Identity != null && p.Identity.IsAuthenticated; }
        public static bool IsArtist(this IPrincipal p) { return p.Role() == AppRoles.Artist; }
        public static bool IsStaff(this IPrincipal p) { var r = p.Role(); return r == AppRoles.Moderator || r == AppRoles.Admin; }
        public static bool IsAdmin(this IPrincipal p) { return p.Role() == AppRoles.Admin; }
    }

    /// <summary>Password hashing (PBKDF2 via ASP.NET Identity's PasswordHasher) and cookie sign-in.</summary>
    public class AuthService
    {
        private static readonly PasswordHasher Hasher = new PasswordHasher();

        public string Hash(string password) { return Hasher.HashPassword(password); }

        public bool Verify(User u, string password)
        {
            return !string.IsNullOrEmpty(u.PasswordHash) && password != null &&
                   Hasher.VerifyHashedPassword(u.PasswordHash, password) != PasswordVerificationResult.Failed;
        }

        public static ClaimsIdentity BuildIdentity(User u)
        {
            var id = new ClaimsIdentity(DefaultAuthenticationTypes.ApplicationCookie, ClaimTypes.Name, ClaimTypes.Role);
            id.AddClaim(new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()));
            id.AddClaim(new Claim(ClaimTypes.Name, u.Username));
            id.AddClaim(new Claim("display_name", u.DisplayName));
            id.AddClaim(new Claim(ClaimTypes.Role, u.Role));
            id.AddClaim(new Claim("stamp", Stamp(u)));
            // required by the antiforgery system
            id.AddClaim(new Claim("http://schemas.microsoft.com/accesscontrolservice/2010/07/claims/identityprovider", "Wavelength"));
            return id;
        }

        /// <summary>Changes whenever role, status, password or display name changes, which refreshes or invalidates old cookies.</summary>
        public static string Stamp(User u)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(u.Role + "|" + u.IsActive + "|" + u.PasswordHash + "|" + u.DisplayName));
                return BitConverter.ToString(bytes).Replace("-", "").Substring(0, 16);
            }
        }

        public void SignIn(HttpContextBase http, User u, bool remember = true)
        {
            var auth = http.GetOwinContext().Authentication;
            auth.SignOut(DefaultAuthenticationTypes.ApplicationCookie);
            auth.SignIn(new AuthenticationProperties { IsPersistent = remember, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14) }, BuildIdentity(u));
        }

        public void SignOut(HttpContextBase http)
        {
            http.GetOwinContext().Authentication.SignOut(DefaultAuthenticationTypes.ApplicationCookie);
        }

        /// <summary>OWIN hook: runs on every request with a cookie. Banned/deleted users are signed out, changed users get fresh claims.</summary>
        public static Task ValidateIdentity(CookieValidateIdentityContext ctx)
        {
            var claim = ctx.Identity.FindFirst(ClaimTypes.NameIdentifier);
            int id;
            if (claim == null || !int.TryParse(claim.Value, out id)) { ctx.RejectIdentity(); return Task.FromResult(0); }
            using (var db = new AppDbContext())
            {
                var u = db.Users.AsNoTracking().FirstOrDefault(x => x.Id == id);
                if (u == null || !u.IsActive)
                {
                    ctx.RejectIdentity();
                    ctx.OwinContext.Authentication.SignOut(DefaultAuthenticationTypes.ApplicationCookie);
                    return Task.FromResult(0);
                }
                var stamp = ctx.Identity.FindFirst("stamp");
                if (stamp == null || stamp.Value != Stamp(u))
                {
                    var fresh = BuildIdentity(u);
                    ctx.ReplaceIdentity(fresh);
                    ctx.OwinContext.Authentication.SignIn(ctx.Properties, fresh);
                }
            }
            return Task.FromResult(0);
        }
    }

    /// <summary>Notifications (new follower, track approved, comment...). The payload is stored as JSON text.</summary>
    public class NotificationService
    {
        private readonly AppDbContext _db;
        public NotificationService(AppDbContext db) { _db = db; }

        public void Add(int userId, string type, object payload, bool isRead = false)
        {
            var json = JsonConvert.SerializeObject(payload);
            if (json.Length > 500) json = json.Substring(0, 500);
            _db.Notifications.Add(new Notification { UserId = userId, Type = type, Payload = json, IsRead = isRead });
        }

        public static Dictionary<string, string> Read(string payload)
        {
            try
            {
                var obj = JObject.Parse(payload);
                return obj.Properties().ToDictionary(p => p.Name, p => p.Value.Type == JTokenType.String ? (string)p.Value : p.Value.ToString());
            }
            catch { return new Dictionary<string, string>(); }
        }

        /// <summary>Human readable text and link for a notification row.</summary>
        public static Tuple<string, string> Describe(Notification n)
        {
            var p = Read(n.Payload);
            Func<string, string> g = k => { string v; return p.TryGetValue(k, out v) ? v : ""; };
            switch (n.Type)
            {
                case "follow": return Tuple.Create(g("name") + " started following you.", "/users/" + g("username"));
                case "track_approved": return Tuple.Create("Your track \"" + g("title") + "\" is live.", "/tracks/" + g("track_id"));
                case "track_rejected": return Tuple.Create("Your track \"" + g("title") + "\" was rejected: " + g("reason"), "/studio/tracks/" + g("track_id"));
                case "track_hidden": return Tuple.Create("Your track \"" + g("title") + "\" was hidden after a report.", "/studio/tracks/" + g("track_id"));
                case "track_restored": return Tuple.Create("Your track \"" + g("title") + "\" is visible again.", "/tracks/" + g("track_id"));
                case "track_pending": return Tuple.Create("New track waiting for review: \"" + g("title") + "\" by " + g("artist") + ".", "/admin/review");
                case "comment": return Tuple.Create(g("name") + " commented on \"" + g("title") + "\".", "/tracks/" + g("track_id") + "#comments");
                case "reply": return Tuple.Create(g("name") + " replied to your comment on \"" + g("title") + "\".", "/tracks/" + g("track_id") + "#comments");
                case "report_resolved": return Tuple.Create("Your report was reviewed and " + g("status") + ". Thanks for keeping Wavelength clean.", (string)null);
                case "artist_verified": return Tuple.Create("Your artist profile is now verified.", "/studio");
                case "role_changed": return Tuple.Create("Your role was changed to " + g("role") + ".", (string)null);
                case "welcome": return Tuple.Create("Welcome to Wavelength. Start by liking a few tracks.", "/explore");
                default: return Tuple.Create(n.Type, (string)null);
            }
        }

        /// <summary>Notifies every active moderator and admin.</summary>
        public void AddToStaff(string type, object payload)
        {
            foreach (var id in _db.Users.Where(u => u.IsActive && (u.Role == AppRoles.Moderator || u.Role == AppRoles.Admin)).Select(u => u.Id).ToList())
                Add(id, type, payload);
        }
    }

    public class AuditService
    {
        private readonly AppDbContext _db;
        public AuditService(AppDbContext db) { _db = db; }

        public void Log(int adminId, string action, string target)
        {
            target = target ?? "";
            _db.AuditLogs.Add(new AuditLog { AdminId = adminId, Action = action, Target = target.Length > 120 ? target.Substring(0, 120) : target });
        }
    }

    /// <summary>Stores uploaded audio and images under ~/uploads/{folder}/ with random file names.</summary>
    public class FileStorage
    {
        public static readonly string[] AudioExt = { ".mp3", ".wav", ".ogg", ".m4a" };
        public static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        public const int MaxAudioBytes = 20 * 1024 * 1024;
        public const int MaxImageBytes = 4 * 1024 * 1024;

        public static bool HasFile(HttpPostedFileBase f) { return f != null && f.ContentLength > 0; }

        public static string ValidateAudio(HttpPostedFileBase f)
        {
            if (!HasFile(f)) return "Choose an audio file.";
            if (!AudioExt.Contains(Path.GetExtension(f.FileName).ToLowerInvariant())) return "Audio must be MP3, WAV, OGG or M4A.";
            if (f.ContentLength > MaxAudioBytes) return "Audio file must be 20 MB or smaller.";
            if (!(f.ContentType.StartsWith("audio/") || f.ContentType == "application/octet-stream")) return "That file does not look like audio.";
            return null;
        }

        /// <summary>Images are optional, so an empty input is valid.</summary>
        public static string ValidateImage(HttpPostedFileBase f)
        {
            if (!HasFile(f)) return null;
            if (!ImageExt.Contains(Path.GetExtension(f.FileName).ToLowerInvariant())) return "Images must be JPG, PNG, WEBP or GIF.";
            if (f.ContentLength > MaxImageBytes) return "Images must be 4 MB or smaller.";
            if (!f.ContentType.StartsWith("image/")) return "That file does not look like an image.";
            return null;
        }

        public string Save(HttpPostedFileBase f, string folder)
        {
            var name = Guid.NewGuid().ToString("N") + Path.GetExtension(f.FileName).ToLowerInvariant();
            var dir = HostingEnvironment.MapPath("~/uploads/" + folder);
            Directory.CreateDirectory(dir);
            f.SaveAs(Path.Combine(dir, name));
            return "/uploads/" + folder + "/" + name;
        }

        public void Delete(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("/uploads/") || url.Contains("..")) return;
            var path = HostingEnvironment.MapPath("~" + url);
            if (path != null && File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>Removes rows that depend on a track, comment, album, playlist or user (FKs have no cascade).</summary>
    public class CleanupService
    {
        private readonly AppDbContext _db;
        public CleanupService(AppDbContext db) { _db = db; }

        private void Sql(string sql, params object[] args) { _db.Database.ExecuteSqlCommand(sql, args); }

        public void DeleteTrack(int trackId)
        {
            Sql("DELETE FROM reports WHERE track_id = @p0 OR comment_id IN (SELECT id FROM comments WHERE track_id = @p0)", trackId);
            Sql("DELETE FROM comments WHERE track_id = @p0 AND parent_id IS NOT NULL", trackId);
            Sql("DELETE FROM comments WHERE track_id = @p0", trackId);
            Sql("DELETE FROM track_genres WHERE track_id = @p0", trackId);
            Sql("DELETE FROM track_likes WHERE track_id = @p0", trackId);
            Sql("DELETE FROM playlist_tracks WHERE track_id = @p0", trackId);
            Sql("DELETE FROM listening_history WHERE track_id = @p0", trackId);
            Sql("DELETE FROM share_links WHERE track_id = @p0", trackId);
            // notifications keep the track id inside the JSON payload; drop the ones that would now link to a 404
            Sql("DELETE FROM notifications WHERE payload LIKE @p0 OR payload LIKE @p1", "%\"track_id\":" + trackId + ",%", "%\"track_id\":" + trackId + "}%");
            Sql("DELETE FROM tracks WHERE id = @p0", trackId);
        }

        public void DeletePlaylist(int playlistId)
        {
            Sql("DELETE FROM playlist_tracks WHERE playlist_id = @p0", playlistId);
            Sql("DELETE FROM saved_playlists WHERE playlist_id = @p0", playlistId);
            Sql("DELETE FROM playlists WHERE id = @p0", playlistId);
        }

        public void DeleteAlbum(int albumId)
        {
            Sql("UPDATE tracks SET album_id = NULL WHERE album_id = @p0", albumId);
            Sql("DELETE FROM saved_albums WHERE album_id = @p0", albumId);
            Sql("DELETE FROM albums WHERE id = @p0", albumId);
        }

        /// <summary>Deletes a listener or artist account and everything it owns.</summary>
        public void DeleteUser(int userId)
        {
            var artist = _db.ArtistProfiles.FirstOrDefault(a => a.UserId == userId);
            if (artist != null)
            {
                foreach (var t in _db.Tracks.Where(t => t.ArtistId == artist.Id).Select(t => t.Id).ToList()) DeleteTrack(t);
                foreach (var a in _db.Albums.Where(a => a.ArtistId == artist.Id).Select(a => a.Id).ToList()) DeleteAlbum(a);
                Sql("DELETE FROM artist_profiles WHERE id = @p0", artist.Id);
            }
            foreach (var p in _db.Playlists.Where(p => p.OwnerId == userId).Select(p => p.Id).ToList()) DeletePlaylist(p);

            const string mine = "SELECT id FROM comments WHERE user_id = @p0";
            const string replies = "SELECT id FROM comments WHERE parent_id IN (SELECT id FROM comments WHERE user_id = @p0)";
            Sql("DELETE FROM reports WHERE reporter_id = @p0 OR reported_user_id = @p0 OR comment_id IN (" + mine + ") OR comment_id IN (" + replies + ")", userId);
            Sql("UPDATE reports SET handled_by = NULL WHERE handled_by = @p0", userId);
            Sql("DELETE FROM comments WHERE id IN (" + replies + ")", userId);
            Sql("DELETE FROM comments WHERE user_id = @p0 AND parent_id IS NOT NULL", userId);
            Sql("DELETE FROM comments WHERE user_id = @p0", userId);

            Sql("DELETE FROM follows WHERE follower_id = @p0 OR followed_id = @p0", userId);
            Sql("DELETE FROM notifications WHERE user_id = @p0", userId);
            Sql("DELETE FROM track_likes WHERE user_id = @p0", userId);
            Sql("DELETE FROM saved_albums WHERE user_id = @p0", userId);
            Sql("DELETE FROM saved_playlists WHERE user_id = @p0", userId);
            Sql("DELETE FROM listening_history WHERE user_id = @p0", userId);
            Sql("DELETE FROM share_links WHERE created_by = @p0", userId);
            Sql("DELETE FROM users WHERE id = @p0", userId);
        }
    }

    public static class Fmt
    {
        public static string Duration(int sec) { return (sec / 60) + ":" + (sec % 60).ToString("00"); }

        public static string Count(long n)
        {
            if (n >= 1000000) return (n / 1000000d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M";
            if (n >= 1000) return (n / 1000d).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K";
            return n.ToString();
        }

        public static string Ago(DateTime utc)
        {
            var d = DateTime.UtcNow - utc;
            if (d.TotalMinutes < 1) return "just now";
            if (d.TotalHours < 1) return (int)d.TotalMinutes + "m ago";
            if (d.TotalDays < 1) return (int)d.TotalHours + "h ago";
            if (d.TotalDays < 30) return (int)d.TotalDays + "d ago";
            return utc.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string Slug(string s)
        {
            var chars = (s ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray();
            var slug = new string(chars);
            while (slug.Contains("--")) slug = slug.Replace("--", "-");
            return slug.Trim('-');
        }

        /// <summary>Picks a text color (ink or paper) that reads well on a background color.</summary>
        public static string On(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return "#0A0A0A";
            try
            {
                var r = Convert.ToInt32(hex.Substring(1, 2), 16); var g = Convert.ToInt32(hex.Substring(3, 2), 16); var b = Convert.ToInt32(hex.Substring(5, 2), 16);
                return (r * 299 + g * 587 + b * 114) / 1000 > 140 ? "#0A0A0A" : "#F2EDE2";
            }
            catch (FormatException) { return "#0A0A0A"; }
        }

        public static readonly string[] Palette = { "#FFE500", "#2B3BFF", "#FF4A1C", "#8CF5C0" };
        public static string ColorFor(int id) { return Palette[Math.Abs(id) % Palette.Length]; }

        private static readonly JsonSerializerSettings CamelCase = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            StringEscapeHandling = StringEscapeHandling.EscapeHtml,
        };

        /// <summary>camelCase JSON (for data-props attributes and the API). Razor HTML-encodes the returned string.</summary>
        public static string Json(object o) { return JsonConvert.SerializeObject(o, CamelCase); }
    }
}
