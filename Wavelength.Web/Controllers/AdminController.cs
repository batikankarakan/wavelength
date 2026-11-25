using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Models;
using Wavelength.Web.Services;

namespace Wavelength.Web.Controllers
{
    /// <summary>Report section 3.4: moderation and administration (7 pages).</summary>
    [RoutePrefix("admin")]
    [Authorize(Roles = AppRoles.Staff)]
    public class AdminController : BaseController
    {
        private const int PageSize = 20;
        private static readonly string[] MessageStatuses = { "new", "read", "replied" };
        private static readonly string[] ReportTabs = { "open", "resolved", "dismissed" };

        private int Me { get { return CurrentUserId.Value; } }

        /// <summary>Sets the active nav item and the badge counts shown in the admin tabs (_AdminNav).</summary>
        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            ViewBag.Nav = "admin";
            if (Request.HttpMethod == "GET" && Request.IsAuthenticated)
            {
                ViewBag.PendingCount = Db.Tracks.Count(t => t.Status == TrackStatus.Pending);
                ViewBag.OpenReportCount = Db.Reports.Count(r => r.Status == "open");
                ViewBag.NewMessageCount = User.IsAdmin() ? Db.ContactMessages.Count(m => m.Status == "new") : 0;
            }
            base.OnActionExecuting(filterContext);
        }

        private static int Pages(int total) { return (int)Math.Ceiling(total / (double)PageSize); }

        // =====================================================================
        // 1. Dashboard
        // =====================================================================
        [HttpGet, Route("")]
        public ActionResult Index()
        {
            var now = DateTime.UtcNow;
            var weekAgo = now.AddDays(-7);
            var monthAgo = now.AddDays(-30);
            var vm = new AdminDashboardVm
            {
                UsersByRole = Db.Users.GroupBy(u => u.Role).Select(g => new { g.Key, C = g.Count() }).ToList().ToDictionary(x => x.Key, x => x.C),
                TotalUsers = Db.Users.Count(),
                BannedUsers = Db.Users.Count(u => !u.IsActive),
                NewUsers7d = Db.Users.Count(u => u.CreatedAt >= weekAgo),
                TracksByStatus = Db.Tracks.GroupBy(t => t.Status).Select(g => new { g.Key, C = g.Count() }).ToList().ToDictionary(x => x.Key, x => x.C),
                Plays7d = Db.ListeningHistory.Count(h => h.PlayedAt >= weekAgo),
                Plays30d = Db.ListeningHistory.Count(h => h.PlayedAt >= monthAgo),
                TotalPlays = Db.Tracks.Sum(t => (long?)t.PlaysCount) ?? 0,
                OpenReports = Db.Reports.Count(r => r.Status == "open"),
                UnreadMessages = Db.ContactMessages.Count(m => m.Status == "new"),
            };

            // plays per day, last 30 days (from listening_history)
            var since = now.Date.AddDays(-29);
            var perDay = Db.ListeningHistory.Where(h => h.PlayedAt >= since)
                .GroupBy(h => DbFunctions.TruncateTime(h.PlayedAt))
                .Select(g => new { Day = g.Key, C = g.Count() })
                .ToList()
                .Where(x => x.Day.HasValue)
                .ToDictionary(x => x.Day.Value, x => x.C);
            for (var d = since; d <= now.Date; d = d.AddDays(1))
            {
                int c;
                perDay.TryGetValue(d, out c);
                vm.PlaysPerDay.Add(new BarPoint { Label = d.ToString("dd"), Sub = d.ToString("d MMM"), Value = c });
            }

            // signups per week, last 8 weeks
            var weekStart = now.Date.AddDays(-7 * 7 - (int)now.DayOfWeek);
            var signups = Db.Users.Where(u => u.CreatedAt >= weekStart).Select(u => u.CreatedAt).ToList();
            for (var w = 0; w < 8; w++)
            {
                var a = weekStart.AddDays(7 * w);
                var b = a.AddDays(7);
                vm.SignupsPerWeek.Add(new BarPoint { Label = a.ToString("d MMM"), Value = signups.Count(c => c >= a && c < b) });
            }

            // top genres by plays
            vm.TopGenres = Db.TrackGenres.Where(tg => tg.Track.Status == TrackStatus.Published)
                .GroupBy(tg => new { tg.Genre.Name, tg.Genre.Color })
                .Select(g => new { g.Key.Name, g.Key.Color, Plays = g.Sum(x => (int?)x.Track.PlaysCount) ?? 0 })
                .ToList()
                .OrderByDescending(g => g.Plays).Take(8)
                .Select(g => new BarPoint { Label = g.Name, Value = g.Plays, Color = g.Color }).ToList();

            vm.RecentAudit = Db.AuditLogs.AsNoTracking().Include(a => a.Admin).OrderByDescending(a => a.CreatedAt).Take(6).ToList();
            return View(vm);
        }

        // =====================================================================
        // 2. Review queue
        // =====================================================================
        private static ReviewItem ToReview(Track t)
        {
            return new ReviewItem
            {
                Id = t.Id, Title = t.Title, ArtistId = t.ArtistId, Artist = t.Artist.StageName, Verified = t.Artist.Verified,
                CoverUrl = t.CoverUrl, DurationSec = t.DurationSec, Lyrics = t.Lyrics, AlbumTitle = t.Album != null ? t.Album.Title : null,
                CreatedAt = t.CreatedAt,
                Genres = t.TrackGenres.OrderBy(g => g.GenreId).Select(g => new GenreTag { Name = g.Genre.Name, Color = g.Genre.Color }).ToList(),
            };
        }

        private IQueryable<Track> ReviewQuery(string status)
        {
            return Db.Tracks.AsNoTracking()
                .Include(t => t.Artist).Include(t => t.Album).Include(t => t.TrackGenres.Select(g => g.Genre))
                .Where(t => t.Status == status);
        }

        [HttpGet, Route("review")]
        public ActionResult Review()
        {
            var vm = new ReviewVm
            {
                Pending = ReviewQuery(TrackStatus.Pending).OrderBy(t => t.CreatedAt).ToList().Select(ToReview).ToList(),
                Hidden = ReviewQuery(TrackStatus.Hidden).OrderByDescending(t => t.CreatedAt).ToList().Select(ToReview).ToList(),
                RejectedCount = Db.Tracks.Count(t => t.Status == TrackStatus.Rejected),
            };
            return View(vm);
        }

        private Track TrackWithArtist(int id)
        {
            return Db.Tracks.Include(t => t.Artist).FirstOrDefault(t => t.Id == id);
        }

        [HttpPost, Route("review/{id:int}/approve")]
        public ActionResult Approve(int id)
        {
            var t = TrackWithArtist(id);
            if (t == null || t.Status != TrackStatus.Pending) { Flash("That track is no longer waiting for review.", true); return Redirect("/admin/review"); }
            t.Status = TrackStatus.Published;
            Notes.Add(t.Artist.UserId, "track_approved", new { title = t.Title, track_id = t.Id });
            Audit.Log(Me, "approve_track", "track #" + t.Id + " " + t.Title);
            Db.SaveChanges();
            Flash("\"" + t.Title + "\" is live.");
            return Redirect("/admin/review");
        }

        [HttpPost, Route("review/{id:int}/reject")]
        public ActionResult Reject(int id, RejectForm f)
        {
            var reason = ((f == null ? null : f.Reason) ?? "").Trim();
            if (reason.Length < 5 || reason.Length > 200) { Flash("A rejection reason of 5 to 200 characters is required.", true); return Redirect("/admin/review"); }
            var t = TrackWithArtist(id);
            if (t == null || t.Status != TrackStatus.Pending) { Flash("That track is no longer waiting for review.", true); return Redirect("/admin/review"); }
            t.Status = TrackStatus.Rejected;
            Notes.Add(t.Artist.UserId, "track_rejected", new { title = t.Title, track_id = t.Id, reason });
            Audit.Log(Me, "reject_track", "track #" + t.Id + " " + t.Title);
            Db.SaveChanges();
            Flash("\"" + t.Title + "\" was rejected. The artist has been notified.");
            return Redirect("/admin/review");
        }

        [HttpPost, Route("tracks/{id:int}/restore")]
        public ActionResult RestoreTrack(int id, string returnUrl)
        {
            var t = TrackWithArtist(id);
            if (t == null || t.Status != TrackStatus.Hidden) { Flash("That track is not hidden.", true); return SafeRedirect(returnUrl, "/admin/review"); }
            t.Status = TrackStatus.Published;
            Notes.Add(t.Artist.UserId, "track_restored", new { title = t.Title, track_id = t.Id });
            Audit.Log(Me, "restore_track", "track #" + t.Id + " " + t.Title);
            Db.SaveChanges();
            Flash("\"" + t.Title + "\" is visible again.");
            return SafeRedirect(returnUrl, "/admin/review");
        }

        // =====================================================================
        // 3. Reports
        // =====================================================================
        [HttpGet, Route("reports")]
        public ActionResult Reports(string tab = "open", int page = 1)
        {
            if (!ReportTabs.Contains(tab)) tab = "open";
            var q = Db.Reports.AsNoTracking().Where(r => r.Status == tab);
            var total = q.Count();
            page = Math.Max(1, page);
            var rows = q.OrderByDescending(r => r.Id).Skip((page - 1) * PageSize).Take(PageSize)
                .Select(r => new ReportRow
                {
                    Id = r.Id, Status = r.Status, Reason = r.Reason,
                    ReporterName = r.Reporter.DisplayName, ReporterUsername = r.Reporter.Username,
                    HandlerName = r.Handler != null ? r.Handler.DisplayName : null,
                    TrackId = r.TrackId, TrackTitle = r.Track != null ? r.Track.Title : null,
                    TrackArtist = r.Track != null ? r.Track.Artist.StageName : null,
                    TrackStatus = r.Track != null ? r.Track.Status : null,
                    CommentId = r.CommentId, CommentBody = r.Comment != null ? r.Comment.Body : null,
                    CommentHidden = r.Comment != null && r.Comment.IsHidden,
                    CommentAuthor = r.Comment != null ? r.Comment.User.DisplayName : null,
                    CommentTrackId = r.Comment != null ? (int?)r.Comment.TrackId : null,
                    CommentTrackTitle = r.Comment != null ? r.Comment.Track.Title : null,
                    UserId = r.ReportedUserId, UserName = r.ReportedUser != null ? r.ReportedUser.DisplayName : null,
                    UserUsername = r.ReportedUser != null ? r.ReportedUser.Username : null,
                    UserRole = r.ReportedUser != null ? r.ReportedUser.Role : null,
                    UserActive = r.ReportedUser != null && r.ReportedUser.IsActive,
                }).ToList();

            var currentTab = tab;
            var vm = new ReportsVm
            {
                Tab = tab,
                Counts = Db.Reports.GroupBy(r => r.Status).Select(g => new { g.Key, C = g.Count() }).ToList().ToDictionary(x => x.Key, x => x.C),
                Reports = rows,
                HiddenComments = Db.Comments.AsNoTracking().Where(c => c.IsHidden).OrderByDescending(c => c.CreatedAt).Take(20)
                    .Select(c => new HiddenCommentRow { Id = c.Id, Body = c.Body, Author = c.User.DisplayName, TrackId = c.TrackId, TrackTitle = c.Track.Title, CreatedAt = c.CreatedAt })
                    .ToList(),
                Pager = new PagerVm { Page = page, TotalPages = Pages(total), Url = p => "/admin/reports?tab=" + currentTab + "&page=" + p },
            };
            return View(vm);
        }

        private Report OpenReport(int id)
        {
            return Db.Reports.Include(r => r.Track.Artist).Include(r => r.Comment).Include(r => r.ReportedUser)
                .FirstOrDefault(r => r.Id == id && r.Status == "open");
        }

        [HttpPost, Route("reports/{id:int}/dismiss")]
        public ActionResult Dismiss(int id)
        {
            var r = OpenReport(id);
            if (r == null) { Flash("That report is already handled.", true); return Redirect("/admin/reports"); }
            r.Status = "dismissed";
            r.HandledBy = Me;
            Notes.Add(r.ReporterId, "report_resolved", new { status = "dismissed" });
            Audit.Log(Me, "dismiss_report", "report #" + r.Id);
            Db.SaveChanges();
            Flash("Report #" + r.Id + " dismissed.");
            return Redirect("/admin/reports");
        }

        [HttpPost, Route("reports/{id:int}/resolve")]
        public ActionResult Resolve(int id)
        {
            var r = OpenReport(id);
            if (r == null) { Flash("That report is already handled.", true); return Redirect("/admin/reports"); }

            string message;
            if (r.Track != null)
            {
                if (r.Track.Status == TrackStatus.Published)
                {
                    r.Track.Status = TrackStatus.Hidden;
                    Notes.Add(r.Track.Artist.UserId, "track_hidden", new { title = r.Track.Title, track_id = r.Track.Id });
                    Audit.Log(Me, "hide_track", "track #" + r.Track.Id + " " + r.Track.Title);
                }
                message = "\"" + r.Track.Title + "\" is hidden from the public catalog.";
            }
            else if (r.Comment != null)
            {
                if (!r.Comment.IsHidden)
                {
                    r.Comment.IsHidden = true;
                    Audit.Log(Me, "hide_comment", "comment #" + r.Comment.Id);
                }
                message = "The comment is hidden.";
            }
            else if (r.ReportedUser != null)
            {
                if (User.IsAdmin() && r.ReportedUser.Role != AppRoles.Admin && r.ReportedUser.Id != Me)
                {
                    if (r.ReportedUser.IsActive)
                    {
                        r.ReportedUser.IsActive = false;
                        Audit.Log(Me, "ban_user", "@" + r.ReportedUser.Username);
                    }
                    message = "@" + r.ReportedUser.Username + " is banned.";
                }
                else
                {
                    message = "Report resolved. Only an admin can ban accounts.";
                }
            }
            else message = "Report resolved.";

            var kind = r.TrackId != null ? "track" : r.CommentId != null ? "comment" : "user";
            r.Status = "resolved";
            r.HandledBy = Me;
            Notes.Add(r.ReporterId, "report_resolved", new { status = "resolved" });
            Audit.Log(Me, "resolve_report", "report #" + r.Id + " (" + kind + ")");
            Db.SaveChanges();
            Flash(message);
            return Redirect("/admin/reports");
        }

        [HttpPost, Route("comments/{id:int}/hide")]
        public ActionResult HideComment(int id, string returnUrl)
        {
            var c = Db.Comments.Find(id);
            if (c == null) return NotFoundView();
            if (!c.IsHidden)
            {
                c.IsHidden = true;
                Audit.Log(Me, "hide_comment", "comment #" + c.Id);
                Db.SaveChanges();
            }
            Flash("Comment hidden.");
            return SafeRedirect(returnUrl, "/admin/reports");
        }

        [HttpPost, Route("comments/{id:int}/restore")]
        public ActionResult RestoreComment(int id, string returnUrl)
        {
            var c = Db.Comments.Find(id);
            if (c == null) return NotFoundView();
            if (c.IsHidden)
            {
                c.IsHidden = false;
                Audit.Log(Me, "restore_comment", "comment #" + c.Id);
                Db.SaveChanges();
            }
            Flash("Comment restored.");
            return SafeRedirect(returnUrl, "/admin/reports");
        }

        // =====================================================================
        // 4. User management (admin only)
        // =====================================================================
        [Authorize(Roles = AppRoles.Admin)]
        [HttpGet, Route("users")]
        public ActionResult Users(string q, string role, string status, int page = 1)
        {
            var query = Db.Users.AsNoTracking().AsQueryable();
            q = q == null ? null : q.Trim();
            if (!string.IsNullOrEmpty(q))
                query = query.Where(u => u.Username.Contains(q) || u.Email.Contains(q) || u.DisplayName.Contains(q)
                                         || u.ArtistProfiles.Any(a => a.StageName.Contains(q)));
            if (!string.IsNullOrEmpty(role) && AppRoles.All.Contains(role)) query = query.Where(u => u.Role == role);
            else role = null;
            if (status == "active") query = query.Where(u => u.IsActive);
            else if (status == "banned") query = query.Where(u => !u.IsActive);
            else status = null;

            var total = query.Count();
            page = Math.Max(1, page);
            var users = query.OrderByDescending(u => u.CreatedAt).Skip((page - 1) * PageSize).Take(PageSize)
                .Select(u => new AdminUserRow
                {
                    Id = u.Id, Username = u.Username, Email = u.Email, DisplayName = u.DisplayName, AvatarUrl = u.AvatarUrl,
                    Role = u.Role, IsActive = u.IsActive, CreatedAt = u.CreatedAt,
                    ArtistId = u.ArtistProfiles.Select(a => (int?)a.Id).FirstOrDefault(),
                    StageName = u.ArtistProfiles.Select(a => a.StageName).FirstOrDefault(),
                    Verified = u.ArtistProfiles.Any(a => a.Verified),
                    Tracks = u.ArtistProfiles.SelectMany(a => a.Tracks).Count(),
                }).ToList();

            var qq = q; var rr = role; var ss = status;
            return View(new AdminUsersVm
            {
                Q = q, Role = role, Status = status, Total = total, Users = users,
                Pager = new PagerVm
                {
                    Page = page, TotalPages = Pages(total),
                    Url = p => "/admin/users?q=" + Uri.EscapeDataString(qq ?? "") + "&role=" + rr + "&status=" + ss + "&page=" + p,
                },
            });
        }

        [Authorize(Roles = AppRoles.Admin)]
        [HttpPost, Route("users/{id:int}/ban")]
        public ActionResult ToggleBan(int id, string returnUrl)
        {
            var u = Db.Users.Find(id);
            if (u == null) return NotFoundView();
            if (u.Id == Me || u.Role == AppRoles.Admin) { Flash("Admins cannot be banned.", true); return SafeRedirect(returnUrl, "/admin/users"); }
            u.IsActive = !u.IsActive;
            Audit.Log(Me, u.IsActive ? "unban_user" : "ban_user", "@" + u.Username);
            Db.SaveChanges();
            Flash(u.IsActive ? "@" + u.Username + " can log in again." : "@" + u.Username + " is banned.");
            return SafeRedirect(returnUrl, "/admin/users");
        }

        [Authorize(Roles = AppRoles.Admin)]
        [HttpPost, Route("users/{id:int}/role")]
        public ActionResult ChangeRole(int id, string role, string returnUrl)
        {
            var u = Db.Users.Find(id);
            if (u == null) return NotFoundView();
            if (role == null || !AppRoles.All.Contains(role)) { Flash("Unknown role.", true); return SafeRedirect(returnUrl, "/admin/users"); }
            if (u.Id == Me) { Flash("You cannot change your own role.", true); return SafeRedirect(returnUrl, "/admin/users"); }
            if (u.Role == role) return SafeRedirect(returnUrl, "/admin/users");

            var old = u.Role;
            u.Role = role;
            if (role == AppRoles.Artist && !Db.ArtistProfiles.Any(a => a.UserId == u.Id))
                Db.ArtistProfiles.Add(new ArtistProfile { UserId = u.Id, StageName = u.DisplayName.Length > 80 ? u.DisplayName.Substring(0, 80) : u.DisplayName });
            Notes.Add(u.Id, "role_changed", new { role });
            Audit.Log(Me, "change_role", "@" + u.Username + ": " + old + " -> " + role);
            Db.SaveChanges();
            Flash("@" + u.Username + " is now " + role + ".");
            return SafeRedirect(returnUrl, "/admin/users");
        }

        [Authorize(Roles = AppRoles.Admin)]
        [HttpPost, Route("users/{id:int}/verify")]
        public ActionResult ToggleVerify(int id, string returnUrl)
        {
            var a = Db.ArtistProfiles.FirstOrDefault(x => x.UserId == id);
            if (a == null) { Flash("This user has no artist profile.", true); return SafeRedirect(returnUrl, "/admin/users"); }
            a.Verified = !a.Verified;
            if (a.Verified) Notes.Add(a.UserId, "artist_verified", new { });
            Audit.Log(Me, a.Verified ? "verify_artist" : "unverify_artist", a.StageName);
            Db.SaveChanges();
            Flash(a.Verified ? a.StageName + " is verified." : a.StageName + " is no longer verified.");
            return SafeRedirect(returnUrl, "/admin/users");
        }

        // =====================================================================
        // 5. Genre management (admin only)
        // =====================================================================
        private List<GenreRow> GenreRows()
        {
            return Db.Genres.AsNoTracking().OrderBy(g => g.Name)
                .Select(g => new GenreRow { Id = g.Id, Name = g.Name, Slug = g.Slug, Color = g.Color, Tracks = g.TrackGenres.Count })
                .ToList();
        }

        [Authorize(Roles = AppRoles.Admin)]
        [HttpGet, Route("genres")]
        public ActionResult Genres()
        {
            return View(new GenresVm { Rows = GenreRows() });
        }

        /// <summary>DataAnnotations (GenreForm) cover length and color; this adds the slug and uniqueness checks.</summary>
        private string ValidateGenre(string name, string color, int? id)
        {
            if (!ModelState.IsValid)
                return ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(e => !string.IsNullOrEmpty(e)) ?? "Check the genre name and color.";
            if (name.Length < 2 || name.Length > 40) return "Genre names need 2 to 40 characters.";
            if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$")) return "Pick a color like #FFE500.";
            var slug = Fmt.Slug(name);
            if (slug == "") return "The name needs at least one letter or number.";
            if (Db.Genres.Any(g => g.Id != id && (g.Name == name || g.Slug == slug))) return "A genre with this name already exists.";
            return null;
        }

        [Authorize(Roles = AppRoles.Admin)]
        [HttpPost, Route("genres")]
        public ActionResult CreateGenre([Bind(Prefix = "NewGenre")] GenreForm f)
        {
            var name = ((f == null ? null : f.Name) ?? "").Trim();
            var color = ((f == null ? null : f.Color) ?? "").Trim().ToUpperInvariant();
            var err = ValidateGenre(name, color, null);
            if (err != null) { Flash(err, true); return Redirect("/admin/genres"); }
            Db.Genres.Add(new Genre { Name = name, Slug = Fmt.Slug(name), Color = color });
            Audit.Log(Me, "create_genre", name);
            Db.SaveChanges();
            Flash("Genre \"" + name + "\" created.");
            return Redirect("/admin/genres");
        }

        [Authorize(Roles = AppRoles.Admin)]
        [HttpPost, Route("genres/{id:int}")]
        public ActionResult UpdateGenre(int id, GenreForm f)
        {
            var g = Db.Genres.Find(id);
            if (g == null) return NotFoundView();
            var name = ((f == null ? null : f.Name) ?? "").Trim();
            var color = ((f == null ? null : f.Color) ?? "").Trim().ToUpperInvariant();
            var err = ValidateGenre(name, color, id);
            if (err != null) { Flash(err, true); return Redirect("/admin/genres"); }
            var old = g.Name;
            g.Name = name; g.Slug = Fmt.Slug(name); g.Color = color;
            Audit.Log(Me, "update_genre", old == name ? name + " (" + color + ")" : old + " -> " + name);
            Db.SaveChanges();
            Flash("Genre \"" + name + "\" saved.");
            return Redirect("/admin/genres");
        }

        [Authorize(Roles = AppRoles.Admin)]
        [HttpPost, Route("genres/{id:int}/delete")]
        public ActionResult DeleteGenre(int id)
        {
            var g = Db.Genres.Find(id);
            if (g == null) return NotFoundView();
            var used = Db.TrackGenres.Count(x => x.GenreId == id);
            if (used > 0) { Flash("\"" + g.Name + "\" is used by " + AdminUi.Plural(used, "track") + " and cannot be deleted.", true); return Redirect("/admin/genres"); }
            Db.Genres.Remove(g);
            Audit.Log(Me, "delete_genre", g.Name);
            Db.SaveChanges();
            Flash("Genre \"" + g.Name + "\" deleted.");
            return Redirect("/admin/genres");
        }

        // =====================================================================
        // 6. Contact inbox (admin only)
        // =====================================================================
        [Authorize(Roles = AppRoles.Admin)]
        [HttpGet, Route("messages")]
        public ActionResult Messages(string status, int? id, int page = 1)
        {
            var q = Db.ContactMessages.AsNoTracking().AsQueryable();
            if (status != null && MessageStatuses.Contains(status)) q = q.Where(m => m.Status == status);
            else status = null;
            var total = q.Count();
            page = Math.Max(1, page);

            ContactMessage open = null;
            if (id != null)
            {
                open = Db.ContactMessages.Find(id.Value);
                if (open != null && open.Status == "new")
                {
                    open.Status = "read";
                    Audit.Log(Me, "update_message", "message #" + open.Id + ": new -> read");
                    Db.SaveChanges();
                    ViewBag.NewMessageCount = Db.ContactMessages.Count(m => m.Status == "new");
                }
            }

            var st = status;
            var vm = new AdminMessagesVm
            {
                Status = status,
                Counts = Db.ContactMessages.GroupBy(m => m.Status).Select(g => new { g.Key, C = g.Count() }).ToList().ToDictionary(x => x.Key, x => x.C),
                Messages = q.OrderByDescending(m => m.CreatedAt).Skip((page - 1) * PageSize).Take(PageSize).ToList(),
                Open = open,
                Pager = new PagerVm { Page = page, TotalPages = Pages(total), Url = p => "/admin/messages?status=" + st + "&page=" + p },
            };
            return View(vm);
        }

        [Authorize(Roles = AppRoles.Admin)]
        [HttpPost, Route("messages/{id:int}/status")]
        public ActionResult MessageStatus(int id, string status, string returnUrl)
        {
            var m = Db.ContactMessages.Find(id);
            if (m == null) return NotFoundView();
            if (status == null || !MessageStatuses.Contains(status)) { Flash("Unknown status.", true); return SafeRedirect(returnUrl, "/admin/messages"); }
            if (m.Status != status)
            {
                Audit.Log(Me, "update_message", "message #" + m.Id + ": " + m.Status + " -> " + status);
                m.Status = status;
                Db.SaveChanges();
            }
            Flash("Message marked as " + status + ".");
            return SafeRedirect(returnUrl, "/admin/messages?id=" + id);
        }

        // =====================================================================
        // 7. Audit log (admin only)
        // =====================================================================
        // The "action" filter is read from the query string: a parameter named "action" would be bound
        // from MVC's own route value (the action name), which broke the filter in an earlier version.
        [Authorize(Roles = AppRoles.Admin)]
        [HttpGet, Route("audit")]
        public ActionResult AuditLog(int? adminId, string q, DateTime? from, DateTime? to, int page = 1)
        {
            var action = Request.QueryString["action"];
            if (action != null) action = action.Trim();

            var query = Db.AuditLogs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action == action);
            if (adminId != null) query = query.Where(a => a.AdminId == adminId);
            q = q == null ? null : q.Trim();
            if (!string.IsNullOrEmpty(q)) query = query.Where(a => a.Target.Contains(q) || a.Action.Contains(q));
            if (from != null) { var fromDay = from.Value.Date; query = query.Where(a => a.CreatedAt >= fromDay); }
            if (to != null) { var toDay = to.Value.Date.AddDays(1); query = query.Where(a => a.CreatedAt < toDay); }

            var total = query.Count();
            page = Math.Max(1, page);
            var rows = query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Skip((page - 1) * PageSize).Take(PageSize)
                .Select(a => new AuditRow
                {
                    Id = a.Id, Admin = a.Admin.DisplayName, AdminUsername = a.Admin.Username, AdminRole = a.Admin.Role,
                    Action = a.Action, Target = a.Target, CreatedAt = a.CreatedAt,
                }).ToList();

            var admins = Db.Users.AsNoTracking().Where(u => Db.AuditLogs.Any(a => a.AdminId == u.Id))
                .OrderBy(u => u.DisplayName).Select(u => new IdName { Id = u.Id, Name = u.DisplayName }).ToList();

            var fa = action; var fq = q; var fid = adminId; var ff = from; var ft = to;
            return View("Audit", new AdminAuditVm
            {
                Action = action, AdminId = adminId, Q = q, From = from, To = to, Total = total, Rows = rows,
                Actions = Db.AuditLogs.Select(a => a.Action).Distinct().OrderBy(a => a).ToList(),
                Admins = admins,
                Pager = new PagerVm
                {
                    Page = page, TotalPages = Pages(total),
                    Url = p => "/admin/audit?action=" + Uri.EscapeDataString(fa ?? "") + "&adminId=" + fid + "&q=" + Uri.EscapeDataString(fq ?? "")
                               + "&from=" + (ff.HasValue ? ff.Value.ToString("yyyy-MM-dd") : "") + "&to=" + (ft.HasValue ? ft.Value.ToString("yyyy-MM-dd") : "") + "&page=" + p,
                },
            });
        }
    }
}
