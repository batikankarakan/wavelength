using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Wavelength.Web.Data;
using Wavelength.Web.Services;

namespace Wavelength.Web.Models
{
    // ---------- view models for the moderation / admin pages (report section 3.4) ----------

    public class AdminNavVm
    {
        public string Active { get; set; }
        public string Title { get; set; }
        public string Sub { get; set; }
    }

    public class BarPoint
    {
        public string Label { get; set; }
        public string Sub { get; set; }
        public int Value { get; set; }
        public string Color { get; set; }
    }

    public class BarsVm
    {
        public BarsVm() { Points = new List<BarPoint>(); Color = "#2B3BFF"; Height = 200; ShowLabels = true; }
        public List<BarPoint> Points { get; set; }
        public string Color { get; set; }
        public int Height { get; set; }
        public bool ShowLabels { get; set; }
    }

    public class AdminDashboardVm
    {
        public AdminDashboardVm()
        {
            UsersByRole = new Dictionary<string, int>();
            TracksByStatus = new Dictionary<string, int>();
            PlaysPerDay = new List<BarPoint>();
            SignupsPerWeek = new List<BarPoint>();
            TopGenres = new List<BarPoint>();
            RecentAudit = new List<AuditLog>();
        }

        public Dictionary<string, int> UsersByRole { get; set; }
        public int TotalUsers { get; set; }
        public int BannedUsers { get; set; }
        public int NewUsers7d { get; set; }
        public Dictionary<string, int> TracksByStatus { get; set; }
        public int Plays7d { get; set; }
        public int Plays30d { get; set; }
        public long TotalPlays { get; set; }
        public int OpenReports { get; set; }
        public int UnreadMessages { get; set; }
        public List<BarPoint> PlaysPerDay { get; set; }
        public List<BarPoint> SignupsPerWeek { get; set; }
        public List<BarPoint> TopGenres { get; set; }
        public List<AuditLog> RecentAudit { get; set; }

        public int Role(string role) { return AdminUi.Get(UsersByRole, role); }
        public int Status(string status) { return AdminUi.Get(TracksByStatus, status); }
    }

    public class GenreTag
    {
        public string Name { get; set; }
        public string Color { get; set; }
    }

    public class ReviewItem
    {
        public ReviewItem() { Genres = new List<GenreTag>(); }

        public int Id { get; set; }
        public string Title { get; set; }
        public int ArtistId { get; set; }
        public string Artist { get; set; }
        public bool Verified { get; set; }
        public string CoverUrl { get; set; }
        public int DurationSec { get; set; }
        public string Lyrics { get; set; }
        public string AlbumTitle { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<GenreTag> Genres { get; set; }
        public string Color { get { return Genres.Count > 0 ? Genres[0].Color : Fmt.ColorFor(Id); } }
    }

    public class ReviewVm
    {
        public ReviewVm() { Pending = new List<ReviewItem>(); Hidden = new List<ReviewItem>(); }
        public List<ReviewItem> Pending { get; set; }
        public List<ReviewItem> Hidden { get; set; }
        public int RejectedCount { get; set; }
    }

    /// <summary>Posted by the reject form of the review queue.</summary>
    public class RejectForm
    {
        [Required(ErrorMessage = "Tell the artist why the track is rejected.")]
        [StringLength(200, MinimumLength = 5, ErrorMessage = "A rejection reason of 5 to 200 characters is required.")]
        public string Reason { get; set; }
    }

    public class ReportRow
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
        public string ReporterName { get; set; }
        public string ReporterUsername { get; set; }
        public string HandlerName { get; set; }
        // track target
        public int? TrackId { get; set; }
        public string TrackTitle { get; set; }
        public string TrackArtist { get; set; }
        public string TrackStatus { get; set; }
        // comment target
        public int? CommentId { get; set; }
        public string CommentBody { get; set; }
        public bool CommentHidden { get; set; }
        public string CommentAuthor { get; set; }
        public int? CommentTrackId { get; set; }
        public string CommentTrackTitle { get; set; }
        // user target
        public int? UserId { get; set; }
        public string UserName { get; set; }
        public string UserUsername { get; set; }
        public string UserRole { get; set; }
        public bool UserActive { get; set; }

        public string Kind { get { return TrackId != null ? "track" : CommentId != null ? "comment" : "user"; } }
    }

    public class HiddenCommentRow
    {
        public int Id { get; set; }
        public string Body { get; set; }
        public string Author { get; set; }
        public int TrackId { get; set; }
        public string TrackTitle { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ReportsVm
    {
        public ReportsVm()
        {
            Tab = "open";
            Counts = new Dictionary<string, int>();
            Reports = new List<ReportRow>();
            HiddenComments = new List<HiddenCommentRow>();
            Pager = new PagerVm();
        }

        public string Tab { get; set; }
        public Dictionary<string, int> Counts { get; set; }
        public List<ReportRow> Reports { get; set; }
        public List<HiddenCommentRow> HiddenComments { get; set; }
        public PagerVm Pager { get; set; }
    }

    public class AdminUserRow
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string AvatarUrl { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? ArtistId { get; set; }
        public string StageName { get; set; }
        public bool Verified { get; set; }
        public int Tracks { get; set; }
    }

    public class AdminUsersVm
    {
        public AdminUsersVm() { Users = new List<AdminUserRow>(); Pager = new PagerVm(); }
        public string Q { get; set; }
        public string Role { get; set; }
        public string Status { get; set; }
        public int Total { get; set; }
        public List<AdminUserRow> Users { get; set; }
        public PagerVm Pager { get; set; }
    }

    public class GenreRow
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
        public string Color { get; set; }
        public int Tracks { get; set; }
    }

    /// <summary>Create / rename a genre. Validated on the client (jQuery unobtrusive) and on the server.</summary>
    public class GenreForm
    {
        public GenreForm() { Color = "#2B3BFF"; }

        [Required(ErrorMessage = "Give the genre a name.")]
        [StringLength(40, MinimumLength = 2, ErrorMessage = "Genre names need 2 to 40 characters.")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Pick a color.")]
        [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Pick a color like #FFE500.")]
        public string Color { get; set; }
    }

    public class GenresVm
    {
        public GenresVm() { Rows = new List<GenreRow>(); NewGenre = new GenreForm(); }
        public List<GenreRow> Rows { get; set; }
        public GenreForm NewGenre { get; set; }
    }

    public class AdminMessagesVm
    {
        public AdminMessagesVm() { Counts = new Dictionary<string, int>(); Messages = new List<ContactMessage>(); Pager = new PagerVm(); }
        public string Status { get; set; }
        public Dictionary<string, int> Counts { get; set; }
        public List<ContactMessage> Messages { get; set; }
        public ContactMessage Open { get; set; }
        public PagerVm Pager { get; set; }

        public string Url(int? id)
        {
            return "/admin/messages?status=" + Status + (id != null ? "&id=" + id : "");
        }
    }

    public class AuditRow
    {
        public int Id { get; set; }
        public string Admin { get; set; }
        public string AdminUsername { get; set; }
        public string AdminRole { get; set; }
        public string Action { get; set; }
        public string Target { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class IdName
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class AdminAuditVm
    {
        public AdminAuditVm() { Actions = new List<string>(); Admins = new List<IdName>(); Rows = new List<AuditRow>(); Pager = new PagerVm(); }
        public string Action { get; set; }
        public int? AdminId { get; set; }
        public string Q { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public List<string> Actions { get; set; }
        public List<IdName> Admins { get; set; }
        public List<AuditRow> Rows { get; set; }
        public int Total { get; set; }
        public PagerVm Pager { get; set; }
    }

    /// <summary>Small display helpers for the admin views.</summary>
    public static class AdminUi
    {
        public static int Get(Dictionary<string, int> d, string key)
        {
            int v;
            return key != null && d.TryGetValue(key, out v) ? v : 0;
        }

        public static string ActionColor(string a)
        {
            a = a ?? "";
            if (a.StartsWith("ban") || a.StartsWith("delete") || a.StartsWith("reject") || a.StartsWith("hide")) return "bg-flame text-paper";
            if (a.StartsWith("approve") || a.StartsWith("restore") || a.StartsWith("unban") || a.StartsWith("verify")) return "bg-mint";
            if (a.StartsWith("create") || a.StartsWith("change") || a.StartsWith("update")) return "bg-volt";
            return "bg-paper";
        }

        public static string TopicColor(string topic)
        {
            switch (topic)
            {
                case "feedback": return "bg-mint";
                case "bug": return "bg-flame text-paper";
                case "collaboration": return "bg-cobalt text-paper";
                default: return "bg-paper";
            }
        }

        public static string KindColor(string kind)
        {
            switch (kind)
            {
                case "track": return "bg-cobalt text-paper";
                case "comment": return "bg-volt";
                default: return "bg-flame text-paper";
            }
        }

        public static string Plural(int n, string word) { return n + " " + word + (n == 1 ? "" : "s"); }
    }
}
