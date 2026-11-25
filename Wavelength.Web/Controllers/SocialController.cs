using System.Linq;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Services;

namespace Wavelength.Web.Controllers
{
    /// <summary>Follow toggle and content reports, used from several public pages.</summary>
    [Authorize]
    public class SocialController : BaseController
    {
        [HttpPost, Route("follow/{userId:int}")]
        public ActionResult Follow(int userId, string returnUrl)
        {
            var uid = CurrentUserId.Value;
            var target = Db.Users.AsNoTracking().FirstOrDefault(u => u.Id == userId && u.IsActive);
            if (target == null) return NotFoundView();
            if (userId == uid)
            {
                Flash("You can't follow yourself.", error: true);
                return SafeRedirect(returnUrl);
            }
            var f = Db.Follows.Find(uid, userId);
            if (f == null)
            {
                Db.Follows.Add(new Follow { FollowerId = uid, FollowedId = userId });
                Notes.Add(userId, "follow", new { name = User.DisplayName() ?? User.Identity.Name, username = User.Identity.Name });
            }
            else Db.Follows.Remove(f);
            Db.SaveChanges();
            Flash(f == null ? "You follow " + target.DisplayName + " now." : "You unfollowed " + target.DisplayName + ".");
            return SafeRedirect(returnUrl);
        }

        [HttpPost, Route("reports")]
        public ActionResult Report(int? trackId, int? commentId, int? reportedUserId, string reason, string returnUrl)
        {
            var uid = CurrentUserId.Value;
            reason = (reason ?? "").Trim();
            var targets = new[] { trackId, commentId, reportedUserId }.Count(x => x != null);
            string error = null;
            if (targets != 1) error = "Pick exactly one thing to report.";
            else if (reason.Length < 5 || reason.Length > 300) error = "Tell us why in 5 to 300 characters.";
            else if (trackId != null && !Db.Tracks.Any(t => t.Id == trackId && t.Status == TrackStatus.Published)) error = "That track no longer exists.";
            else if (commentId != null && !Db.Comments.Any(c => c.Id == commentId && !c.IsHidden)) error = "That comment no longer exists.";
            else if (reportedUserId != null && !Db.Users.Any(u => u.Id == reportedUserId)) error = "That user no longer exists.";
            else if (reportedUserId == uid) error = "You can't report yourself.";
            else if (Db.Reports.Any(r => r.ReporterId == uid && r.Status == "open" && r.TrackId == trackId && r.CommentId == commentId && r.ReportedUserId == reportedUserId))
                error = "You already reported this. A moderator will look at it soon.";
            if (error != null)
            {
                Flash(error, error: true);
                return SafeRedirect(returnUrl);
            }
            Db.Reports.Add(new Report { ReporterId = uid, TrackId = trackId, CommentId = commentId, ReportedUserId = reportedUserId, Reason = reason, Status = "open" });
            Db.SaveChanges();
            Flash("Thanks. A moderator will review your report.");
            return SafeRedirect(returnUrl);
        }
    }
}
