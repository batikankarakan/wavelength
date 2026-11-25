using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Wavelength.Web.Data;
using Wavelength.Web.Infrastructure;
using Wavelength.Web.Models;

namespace Wavelength.Web.Controllers
{
    /// <summary>Public pages 1-3 and 17: Home, About, Contact, Terms and Privacy.</summary>
    public class PagesController : BaseController
    {
        [HttpGet, Route("")]
        public ActionResult Home()
        {
            var uid = CurrentUserId;
            var weekAgo = DateTime.UtcNow.AddDays(-7);
            var vm = new HomeVm
            {
                Trending = Db.Tracks.AsNoTracking().Published().OrderByDescending(t => t.PlaysCount).Take(8).ToItems().ToList().WithLikes(Db, uid),
                Listeners = Db.Users.Count(u => u.IsActive),
                Tracks = Db.Tracks.Count(t => t.Status == TrackStatus.Published),
                Plays = Db.Tracks.Where(t => t.Status == TrackStatus.Published).Sum(t => (long?)t.PlaysCount) ?? 0,
                Countries = Db.ArtistProfiles.Where(a => a.Country != null).Select(a => a.Country).Distinct().Count(),
                SavesToday = Db.TrackLikes.Count(l => l.CreatedAt > weekAgo),
            };
            vm.Genres = new List<string> { "All" }.Concat(vm.Trending.Where(t => t.Genre != null).Select(t => t.Genre).Distinct()).ToList();
            ViewBag.Nav = "home";
            return View(vm);
        }

        [HttpGet, Route("about")]
        public ActionResult About() { return View(); }

        [HttpGet, Route("contact")]
        public ActionResult Contact() { return View(new ContactForm()); }

        [HttpPost, Route("contact")]
        public ActionResult Contact(ContactForm f)
        {
            if (!ContactForm.Topics.Any(t => t.Item1 == f.Topic)) ModelState.AddModelError("Topic", "Pick a topic.");
            if (!ModelState.IsValid) return View(f);
            Db.ContactMessages.Add(new ContactMessage
            {
                Name = f.Name.Trim(), Email = f.Email.Trim().ToLowerInvariant(), Topic = f.Topic, Message = f.Message.Trim(),
            });
            Db.SaveChanges();
            TempData["SentName"] = f.Name.Trim().Split(' ')[0];
            TempData["SentEmail"] = f.Email.Trim();
            return Redirect("/contact?sent=1");
        }

        [HttpGet, Route("terms")]
        public ActionResult Terms() { return View(); }
    }
}
