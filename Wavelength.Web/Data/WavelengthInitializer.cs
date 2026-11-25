using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using Wavelength.Web.Services;

namespace Wavelength.Web.Data
{
    /// <summary>
    /// Creates the database (re-creates it when the model changes), adds the CHECK constraints and loads
    /// demo data so every page has something to show. All demo accounts use the password "Wavelength123!".
    /// </summary>
    public class WavelengthInitializer : DropCreateDatabaseIfModelChanges<AppDbContext>
    {
        public const string DemoPassword = "Wavelength123!";

        protected override void Seed(AppDbContext db)
        {
            AddConstraints(db);
            SeedData(db);
        }

        private static void AddConstraints(AppDbContext db)
        {
            var sql = new[]
            {
                "ALTER TABLE users ADD CONSTRAINT ck_users_role CHECK (role IN ('listener','artist','moderator','admin'))",
                "ALTER TABLE albums ADD CONSTRAINT ck_albums_type CHECK (type IN ('single','ep','album'))",
                "ALTER TABLE tracks ADD CONSTRAINT ck_tracks_status CHECK (status IN ('draft','pending','published','rejected','hidden'))",
                "ALTER TABLE reports ADD CONSTRAINT ck_reports_status CHECK (status IN ('open','resolved','dismissed'))",
                "ALTER TABLE reports ADD CONSTRAINT ck_reports_one_target CHECK ((CASE WHEN track_id IS NULL THEN 0 ELSE 1 END + CASE WHEN comment_id IS NULL THEN 0 ELSE 1 END + CASE WHEN reported_user_id IS NULL THEN 0 ELSE 1 END) = 1)",
                "ALTER TABLE contact_messages ADD CONSTRAINT ck_contact_status CHECK (status IN ('new','read','replied'))",
            };
            foreach (var s in sql) db.Database.ExecuteSqlCommand(s);
        }

        public static void SeedData(AppDbContext db)
        {
            var auth = new AuthService();
            var rnd = new Random(42);
            var now = DateTime.UtcNow;
            Func<double, DateTime> ago = days => now.AddDays(-days).AddMinutes(-rnd.Next(0, 600));

            // ---------- genres ----------
            var genreDefs = new[]
            {
                Tuple.Create("Electronic", "#FFE500"), Tuple.Create("Hip-Hop", "#FF4A1C"), Tuple.Create("Indie", "#2B3BFF"),
                Tuple.Create("Lo-fi", "#8CF5C0"), Tuple.Create("Rock", "#0A0A0A"), Tuple.Create("Jazz", "#C9A2FF"),
                Tuple.Create("Ambient", "#A7D8FF"), Tuple.Create("Synthwave", "#FF7AD9"), Tuple.Create("Pop", "#FFB13B"),
                Tuple.Create("House", "#7CFF6B"),
            };
            var genres = genreDefs.Select(g => new Genre { Name = g.Item1, Slug = Fmt.Slug(g.Item1), Color = g.Item2 }).ToList();
            db.Genres.AddRange(genres);
            Func<string, Genre> G = name => genres.First(g => g.Name == name);

            // ---------- users ----------
            Func<string, string, string, string, string, double, User> newUser = (username, email, name, role, bio, daysAgo) =>
            {
                var u = new User { Username = username, Email = email, DisplayName = name, Role = role, Bio = bio, CreatedAt = ago(daysAgo) };
                u.PasswordHash = auth.Hash(DemoPassword);
                db.Users.Add(u);
                return u;
            };

            var admin = newUser("admin", "admin@wavelength.app", "Wave Admin", AppRoles.Admin, "Keeps the lights on.", 60);
            var mod = newUser("moderator", "mod@wavelength.app", "Mod Squad", AppRoles.Moderator, "Reviews every upload with headphones on.", 58);
            var listeners = new List<User>
            {
                newUser("listener", "listener@wavelength.app", "Demo Listener", AppRoles.Listener, "Here for the bass lines.", 50),
                newUser("emre", "emre@wavelength.app", "Emre Men", AppRoles.Listener, "Group leader. Playlist hoarder.", 45),
                newUser("ibrahim", "ibrahim@wavelength.app", "İbrahim Mutlu", AppRoles.Listener, "Drew the ER diagram, now draws playlists.", 44),
                newUser("huseyin", "huseyin@wavelength.app", "Hüseyin Yiğit Efil", AppRoles.Listener, "UML by day, lo-fi by night.", 44),
                newUser("batikan", "batikan@wavelength.app", "Batıkan Karakan", AppRoles.Listener, "Made the landing page. Loves loud yellow.", 43),
                newUser("deniz", "deniz@example.com", "Deniz Aksoy", AppRoles.Listener, null, 30),
                newUser("maya", "maya@example.com", "Maya Lin", AppRoles.Listener, "Synthwave or nothing.", 25),
                newUser("leo", "leo@example.com", "Leo Brandt", AppRoles.Listener, null, 12),
            };
            var troll = newUser("spammy", "spam@example.com", "Spammy McSpam", AppRoles.Listener, "follow me 4 follow", 9);
            listeners.Add(troll);

            // ---------- artists ----------
            var artistDefs = new[]
            {
                new { U = "kora", Stage = "Kora Vale", Country = "Norway", Verified = true, Bio = "Cold synths, warm kicks. Producing from a cabin outside Bergen." },
                new { U = "hollowkeys", Stage = "The Hollow Keys", Country = "United Kingdom", Verified = true, Bio = "Four-piece indie band from Leeds. Guitars, tape and too much reverb." },
                new { U = "mio", Stage = "Mio Tanaka", Country = "Japan", Verified = true, Bio = "Lo-fi beats recorded next to a rainy window in Osaka." },
                new { U = "djatlas", Stage = "DJ Atlas", Country = "United States", Verified = false, Bio = "Boom bap with a skyline view." },
                new { U = "novadrift", Stage = "Nova Drift", Country = "Germany", Verified = true, Bio = "Synthwave for night drives that never end." },
                new { U = "lumen", Stage = "Lumen", Country = "Türkiye", Verified = false, Bio = "Bedroom pop from Istanbul." },
                new { U = "blackember", Stage = "Black Ember", Country = "Canada", Verified = false, Bio = "Riffs heavy enough to bend the floor." },
                new { U = "selin", Stage = "Selin Ay", Country = "Türkiye", Verified = false, Bio = "Jazz piano and ambient textures." },
            };
            var artists = new Dictionary<string, ArtistProfile>();
            foreach (var a in artistDefs)
            {
                var user = newUser(a.U, a.U + "@wavelength.app", a.Stage, AppRoles.Artist, a.Bio, 55 - artists.Count);
                user.AvatarUrl = "/media/covers/artist-" + a.U + ".svg";
                var p = new ArtistProfile { User = user, StageName = a.Stage, Country = a.Country, Verified = a.Verified, Website = "https://" + a.U + ".example.com", BannerUrl = "/media/covers/banner-" + a.U + ".svg" };
                db.ArtistProfiles.Add(p);
                artists[a.U] = p;
            }
            db.SaveChanges();

            // ---------- albums ----------
            Func<string, string, string, int, Album> newAlbum = (artist, title, type, daysAgo) =>
            {
                var a = new Album { Artist = artists[artist], Title = title, Type = type, CoverUrl = "/media/covers/album-" + Fmt.Slug(title) + ".svg", ReleaseDate = now.AddDays(-daysAgo).Date };
                db.Albums.Add(a);
                return a;
            };
            var nightTransit = newAlbum("kora", "Night Transit", "album", 40);
            var rainyTapes = newAlbum("mio", "Rainy Window Tapes", "ep", 35);
            var chromeHearts = newAlbum("novadrift", "Chrome Hearts", "album", 32);
            var sugarEp = newAlbum("lumen", "Sugar Signal", "ep", 20);
            var rustSingle = newAlbum("blackember", "Rust & Thunder", "single", 28);
            var blueHour = newAlbum("selin", "Blue Hour", "ep", 18);

            // ---------- tracks (audio in /media/audio, generated for the demo) ----------
            var lyrics = new Dictionary<string, string>
            {
                { "neon-highway", "Headlights paint the overpass\nWe never learned to slow down\nNeon highway, take me past\nThe edges of this sleeping town\n\nTurn it up, turn it up\nUntil the city hums along" },
                { "midnight-static", "There's a radio left on in the hall\nPlaying songs that nobody calls\nMidnight static, say my name\nEvery channel sounds the same" },
                { "concrete-crown", "Built a throne out of cracked pavement\nEvery block knows my statement\nConcrete crown, I wear it heavy\nHeadphones on, the beat is steady" },
                { "paper-planes-at-3am", "Folding letters I won't send\nThrowing them out to the end\nPaper planes at 3AM\nLanding where the night begins" },
                { "sugar-signal", "You send a signal, sweet like sugar\nI pick it up from miles away\nTurn the dial a little further\nAnd let the chorus stay" },
                { "rust-and-thunder", "Rust on the gate, thunder in the chest\nWe were never built to rest" },
                { "subway-poems", "Writing verses on the late train\nInk smudged by the window rain\nSubway poems, read them loud\nLost inside the moving crowd" },
            };
            var defs = new[]
            {
                new { Slug = "neon-highway", Title = "Neon Highway", Artist = "kora", Genres = new[] { "Electronic" }, Dur = 64, Album = nightTransit, Status = TrackStatus.Published, Days = 40 },
                new { Slug = "midnight-static", Title = "Midnight Static", Artist = "hollowkeys", Genres = new[] { "Indie", "Rock" }, Dur = 58, Album = (Album)null, Status = TrackStatus.Published, Days = 38 },
                new { Slug = "slow-burn-rain", Title = "Slow Burn Rain", Artist = "mio", Genres = new[] { "Lo-fi" }, Dur = 60, Album = rainyTapes, Status = TrackStatus.Published, Days = 35 },
                new { Slug = "concrete-crown", Title = "Concrete Crown", Artist = "djatlas", Genres = new[] { "Hip-Hop" }, Dur = 56, Album = (Album)null, Status = TrackStatus.Published, Days = 34 },
                new { Slug = "gravity-wells", Title = "Gravity Wells", Artist = "novadrift", Genres = new[] { "Electronic", "Synthwave" }, Dur = 70, Album = chromeHearts, Status = TrackStatus.Published, Days = 32 },
                new { Slug = "paper-planes-at-3am", Title = "Paper Planes at 3AM", Artist = "lumen", Genres = new[] { "Indie", "Pop" }, Dur = 54, Album = sugarEp, Status = TrackStatus.Published, Days = 20 },
                new { Slug = "rust-and-thunder", Title = "Rust & Thunder", Artist = "blackember", Genres = new[] { "Rock" }, Dur = 52, Album = rustSingle, Status = TrackStatus.Published, Days = 28 },
                new { Slug = "coffee-cloud", Title = "Coffee Cloud", Artist = "mio", Genres = new[] { "Lo-fi", "Jazz" }, Dur = 58, Album = rainyTapes, Status = TrackStatus.Published, Days = 35 },
                new { Slug = "glass-elevator", Title = "Glass Elevator", Artist = "novadrift", Genres = new[] { "Synthwave" }, Dur = 62, Album = chromeHearts, Status = TrackStatus.Published, Days = 32 },
                new { Slug = "blue-hour-avenue", Title = "Blue Hour Avenue", Artist = "selin", Genres = new[] { "Jazz" }, Dur = 60, Album = blueHour, Status = TrackStatus.Published, Days = 18 },
                new { Slug = "tidal-memory", Title = "Tidal Memory", Artist = "selin", Genres = new[] { "Ambient" }, Dur = 66, Album = blueHour, Status = TrackStatus.Published, Days = 18 },
                new { Slug = "sugar-signal", Title = "Sugar Signal", Artist = "lumen", Genres = new[] { "Pop" }, Dur = 54, Album = sugarEp, Status = TrackStatus.Published, Days = 20 },
                new { Slug = "warehouse-sunrise", Title = "Warehouse Sunrise", Artist = "kora", Genres = new[] { "House", "Electronic" }, Dur = 64, Album = nightTransit, Status = TrackStatus.Published, Days = 40 },
                new { Slug = "static-bloom", Title = "Static Bloom", Artist = "mio", Genres = new[] { "Lo-fi" }, Dur = 56, Album = rainyTapes, Status = TrackStatus.Published, Days = 35 },
                new { Slug = "iron-lullaby", Title = "Iron Lullaby", Artist = "blackember", Genres = new[] { "Rock" }, Dur = 50, Album = (Album)null, Status = TrackStatus.Published, Days = 10 },
                new { Slug = "velvet-circuit", Title = "Velvet Circuit", Artist = "novadrift", Genres = new[] { "Synthwave", "Electronic" }, Dur = 60, Album = chromeHearts, Status = TrackStatus.Published, Days = 32 },
                new { Slug = "subway-poems", Title = "Subway Poems", Artist = "djatlas", Genres = new[] { "Hip-Hop" }, Dur = 56, Album = (Album)null, Status = TrackStatus.Published, Days = 15 },
                new { Slug = "northern-drift", Title = "Northern Drift", Artist = "kora", Genres = new[] { "Ambient" }, Dur = 62, Album = nightTransit, Status = TrackStatus.Published, Days = 40 },
                new { Slug = "honey-tape", Title = "Honey Tape", Artist = "lumen", Genres = new[] { "Pop", "Indie" }, Dur = 52, Album = sugarEp, Status = TrackStatus.Published, Days = 20 },
                new { Slug = "after-hours-club", Title = "After Hours Club", Artist = "kora", Genres = new[] { "House" }, Dur = 60, Album = (Album)null, Status = TrackStatus.Pending, Days = 1 },
                new { Slug = "moth-to-the-flame", Title = "Moth to the Flame", Artist = "hollowkeys", Genres = new[] { "Indie" }, Dur = 56, Album = (Album)null, Status = TrackStatus.Pending, Days = 2 },
                new { Slug = "kintsugi", Title = "Kintsugi", Artist = "selin", Genres = new[] { "Jazz" }, Dur = 58, Album = (Album)null, Status = TrackStatus.Rejected, Days = 5 },
            };
            var tracks = new List<Track>();
            foreach (var d in defs)
            {
                string text;
                lyrics.TryGetValue(d.Slug, out text);
                var t = new Track
                {
                    Artist = artists[d.Artist], Album = d.Album, Title = d.Title, AudioUrl = "/media/audio/" + d.Slug + ".mp3",
                    CoverUrl = d.Album != null ? d.Album.CoverUrl : "/media/covers/" + d.Slug + ".svg", DurationSec = d.Dur, Status = d.Status,
                    Lyrics = text, CreatedAt = ago(d.Days),
                };
                foreach (var g in d.Genres) t.TrackGenres.Add(new TrackGenre { Genre = G(g) });
                db.Tracks.Add(t);
                tracks.Add(t);
            }
            var draft = new Track { Artist = artists["djatlas"], Title = "Untitled Demo 04", AudioUrl = "/media/audio/subway-poems.mp3", DurationSec = 56, Status = TrackStatus.Draft, CreatedAt = ago(0.5) };
            draft.TrackGenres.Add(new TrackGenre { Genre = G("Hip-Hop") });
            db.Tracks.Add(draft);
            db.SaveChanges();

            var published = tracks.Where(t => t.Status == TrackStatus.Published).ToList();
            Func<string, Track> T = title => tracks.First(t => t.Title == title);

            // ---------- listening history (last 30 days) and play counts ----------
            var popularity = published.ToDictionary(t => t.Id, t => 0.3 + rnd.NextDouble());
            var allListeners = listeners.Concat(new[] { admin, mod }).ToList();
            var history = new List<ListeningHistory>();
            foreach (var u in allListeners)
            {
                var taste = published.OrderBy(x => rnd.Next()).Take(rnd.Next(5, 11)).ToList();
                var plays = rnd.Next(25, 70);
                for (var i = 0; i < plays; i++)
                {
                    var t = rnd.NextDouble() < 0.75 ? taste[rnd.Next(taste.Count)] : published[rnd.Next(published.Count)];
                    if (rnd.NextDouble() > popularity[t.Id]) t = taste[rnd.Next(taste.Count)];
                    var played = now.AddDays(-Math.Pow(rnd.NextDouble(), 1.4) * 30).AddMinutes(-rnd.Next(0, 1440));
                    if (played < t.CreatedAt) played = t.CreatedAt.AddHours(rnd.Next(1, 48));
                    if (played > now) played = now.AddMinutes(-rnd.Next(5, 300));
                    history.Add(new ListeningHistory { UserId = u.Id, TrackId = t.Id, PlayedAt = played, SecondsListened = rnd.Next(30, t.DurationSec + 1) });
                }
            }
            db.ListeningHistory.AddRange(history);
            foreach (var t in published)
                t.PlaysCount = history.Count(h => h.TrackId == t.Id) + (int)(popularity[t.Id] * rnd.Next(800, 4000)); // + plays from visitors

            // ---------- likes and follows ----------
            foreach (var u in allListeners)
                foreach (var t in published.OrderBy(x => rnd.Next()).Take(rnd.Next(3, 10)))
                    db.TrackLikes.Add(new TrackLike { UserId = u.Id, TrackId = t.Id, CreatedAt = ago(rnd.NextDouble() * 25) });
            foreach (var u in allListeners)
                foreach (var a in artists.Values.OrderBy(x => rnd.Next()).Take(rnd.Next(2, 6)))
                    db.Follows.Add(new Follow { FollowerId = u.Id, FollowedId = a.UserId, CreatedAt = ago(rnd.NextDouble() * 30) });
            db.Follows.Add(new Follow { FollowerId = listeners[1].Id, FollowedId = listeners[4].Id, CreatedAt = ago(10) });
            db.Follows.Add(new Follow { FollowerId = listeners[4].Id, FollowedId = listeners[1].Id, CreatedAt = ago(9) });
            db.Follows.Add(new Follow { FollowerId = listeners[2].Id, FollowedId = listeners[0].Id, CreatedAt = ago(4) });
            db.Follows.Add(new Follow { FollowerId = artists["mio"].UserId, FollowedId = artists["selin"].UserId, CreatedAt = ago(15) });

            // ---------- playlists ----------
            Func<User, string, string, bool, Track[], int, Playlist> newPlaylist = (owner, title, desc, isPublic, items, daysAgo) =>
            {
                var p = new Playlist { OwnerId = owner.Id, Title = title, Description = desc, IsPublic = isPublic, CreatedAt = ago(daysAgo), CoverUrl = "/media/covers/playlist-" + Fmt.Slug(title) + ".svg" };
                var pos = 1;
                foreach (var t in items) p.PlaylistTracks.Add(new PlaylistTrack { TrackId = t.Id, Position = pos++, AddedAt = ago(daysAgo - 0.5) });
                db.Playlists.Add(p);
                return p;
            };
            var p1 = newPlaylist(listeners[0], "Late Night Coding", "Beats to ship features to. No vocals, no distractions.", true,
                new[] { T("Slow Burn Rain"), T("Coffee Cloud"), T("Static Bloom"), T("Northern Drift"), T("Tidal Memory"), T("Glass Elevator") }, 20);
            var p2 = newPlaylist(listeners[1], "Group Project Panic", "For the night before the deadline.", true,
                new[] { T("Rust & Thunder"), T("Neon Highway"), T("Concrete Crown"), T("Iron Lullaby"), T("Warehouse Sunrise") }, 14);
            var p3 = newPlaylist(listeners[6], "Chrome & Neon", "Synthwave only. Sunglasses required.", true,
                new[] { T("Gravity Wells"), T("Glass Elevator"), T("Velvet Circuit"), T("Neon Highway") }, 12);
            var p4 = newPlaylist(listeners[4], "Yellow Mornings", "Bright songs for slow mornings.", true,
                new[] { T("Sugar Signal"), T("Honey Tape"), T("Paper Planes at 3AM"), T("Blue Hour Avenue"), T("Coffee Cloud") }, 8);
            newPlaylist(listeners[4], "Secret Guilty Pleasures", "Nobody needs to know.", false,
                new[] { T("Sugar Signal"), T("Warehouse Sunrise") }, 6);
            var p6 = newPlaylist(listeners[3], "Rainy Window", "Lo-fi and jazz for grey afternoons.", true,
                new[] { T("Slow Burn Rain"), T("Blue Hour Avenue"), T("Coffee Cloud"), T("Static Bloom"), T("Tidal Memory") }, 5);
            db.SaveChanges();

            // ---------- saved albums and playlists ----------
            db.SavedAlbums.Add(new SavedAlbum { UserId = listeners[0].Id, AlbumId = nightTransit.Id, SavedAt = ago(15) });
            db.SavedAlbums.Add(new SavedAlbum { UserId = listeners[0].Id, AlbumId = rainyTapes.Id, SavedAt = ago(12) });
            db.SavedAlbums.Add(new SavedAlbum { UserId = listeners[1].Id, AlbumId = chromeHearts.Id, SavedAt = ago(9) });
            db.SavedAlbums.Add(new SavedAlbum { UserId = listeners[4].Id, AlbumId = sugarEp.Id, SavedAt = ago(7) });
            db.SavedAlbums.Add(new SavedAlbum { UserId = listeners[6].Id, AlbumId = chromeHearts.Id, SavedAt = ago(11) });
            db.SavedPlaylists.Add(new SavedPlaylist { UserId = listeners[0].Id, PlaylistId = p3.Id, SavedAt = ago(6) });
            db.SavedPlaylists.Add(new SavedPlaylist { UserId = listeners[0].Id, PlaylistId = p4.Id, SavedAt = ago(3) });
            db.SavedPlaylists.Add(new SavedPlaylist { UserId = listeners[1].Id, PlaylistId = p1.Id, SavedAt = ago(10) });
            db.SavedPlaylists.Add(new SavedPlaylist { UserId = listeners[2].Id, PlaylistId = p6.Id, SavedAt = ago(2) });
            db.SavedPlaylists.Add(new SavedPlaylist { UserId = listeners[5].Id, PlaylistId = p2.Id, SavedAt = ago(4) });

            // ---------- comments (with replies) ----------
            Func<User, Track, string, double, Comment, Comment> say = (u, t, body, daysAgo, parent) =>
            {
                var c = new Comment { UserId = u.Id, TrackId = t.Id, Body = body, CreatedAt = ago(daysAgo), Parent = parent };
                db.Comments.Add(c);
                return c;
            };
            var c1 = say(listeners[0], T("Neon Highway"), "That drop at 0:40 got me out of my chair.", 12, null);
            say(artists["kora"].User, T("Neon Highway"), "Glad you caught it! It was the last thing I added.", 11, c1);
            say(listeners[6], T("Neon Highway"), "Perfect for night drives.", 8, null);
            var c2 = say(listeners[3], T("Slow Burn Rain"), "Rain + this track = finished my UML diagrams.", 9, null);
            say(listeners[2], T("Slow Burn Rain"), "Same here, it's on repeat.", 8, c2);
            say(listeners[1], T("Rust & Thunder"), "Turned it up to 11. Neighbours did not approve.", 6, null);
            say(listeners[4], T("Sugar Signal"), "This chorus lives in my head rent free.", 4, null);
            say(listeners[5], T("Concrete Crown"), "Boom bap is back.", 3, null);
            var spam = say(troll, T("Gravity Wells"), "FREE FOLLOWERS!!! visit my profile now!!!", 2, null);
            say(troll, T("Sugar Signal"), "follow 4 follow, click my link", 1.5, null);
            say(listeners[7], T("Glass Elevator"), "The synth line sounds like 1986 in the best way.", 1, null);
            db.SaveChanges();

            // ---------- reports ----------
            db.Reports.Add(new Report { ReporterId = listeners[0].Id, CommentId = spam.Id, Reason = "Spam: advertising followers in comments.", Status = "open" });
            db.Reports.Add(new Report { ReporterId = listeners[2].Id, ReportedUserId = troll.Id, Reason = "This account only posts spam links.", Status = "open" });
            db.Reports.Add(new Report { ReporterId = listeners[5].Id, TrackId = T("Subway Poems").Id, Reason = "Sounds like it samples another song without credit.", Status = "open" });
            db.Reports.Add(new Report { ReporterId = listeners[1].Id, TrackId = T("Iron Lullaby").Id, Reason = "Cover image is too dark?", Status = "dismissed", HandledBy = mod.Id });

            // ---------- share links ----------
            db.ShareLinks.Add(new ShareLink { TrackId = T("Neon Highway").Id, CreatedBy = listeners[0].Id, Token = "neonhwy1", ClickCount = 37, CreatedAt = ago(10) });
            db.ShareLinks.Add(new ShareLink { TrackId = T("Sugar Signal").Id, CreatedBy = listeners[4].Id, Token = "sugar042", ClickCount = 12, CreatedAt = ago(5) });
            db.ShareLinks.Add(new ShareLink { TrackId = T("Slow Burn Rain").Id, CreatedBy = listeners[3].Id, Token = "rainlofi", ClickCount = 21, CreatedAt = ago(8) });

            // ---------- notifications ----------
            var notes = new NotificationService(db);
            foreach (var u in listeners) notes.Add(u.Id, "welcome", new { }, isRead: true);
            notes.Add(listeners[0].Id, "follow", new { name = listeners[2].DisplayName, username = listeners[2].Username });
            notes.Add(listeners[0].Id, "reply", new { name = "Kora Vale", title = "Neon Highway", track_id = T("Neon Highway").Id });
            notes.Add(artists["kora"].UserId, "comment", new { name = listeners[6].DisplayName, title = "Neon Highway", track_id = T("Neon Highway").Id });
            notes.Add(artists["kora"].UserId, "track_approved", new { title = "Warehouse Sunrise", track_id = T("Warehouse Sunrise").Id });
            notes.Add(artists["selin"].UserId, "track_rejected", new { title = "Kintsugi", track_id = T("Kintsugi").Id, reason = "The audio clips at the end. Please fix the master and resubmit." });
            notes.Add(listeners[1].Id, "report_resolved", new { status = "dismissed" });
            foreach (var s in new[] { mod, admin })
            {
                notes.Add(s.Id, "track_pending", new { title = "After Hours Club", artist = "Kora Vale", track_id = T("After Hours Club").Id });
                notes.Add(s.Id, "track_pending", new { title = "Moth to the Flame", artist = "The Hollow Keys", track_id = T("Moth to the Flame").Id });
            }

            // ---------- audit log ----------
            var logs = new List<AuditLog>();
            foreach (var t in published.Take(8)) logs.Add(new AuditLog { AdminId = mod.Id, Action = "approve_track", Target = "track #" + t.Id + " " + t.Title });
            logs.Add(new AuditLog { AdminId = mod.Id, Action = "reject_track", Target = "track #" + T("Kintsugi").Id + " Kintsugi" });
            logs.Add(new AuditLog { AdminId = mod.Id, Action = "dismiss_report", Target = "report on track Iron Lullaby" });
            logs.Add(new AuditLog { AdminId = admin.Id, Action = "verify_artist", Target = "Kora Vale" });
            logs.Add(new AuditLog { AdminId = admin.Id, Action = "verify_artist", Target = "Mio Tanaka" });
            logs.Add(new AuditLog { AdminId = admin.Id, Action = "create_genre", Target = "House" });
            logs.Add(new AuditLog { AdminId = admin.Id, Action = "change_role", Target = "moderator: listener -> moderator" });
            for (var i = 0; i < logs.Count; i++) logs[i].CreatedAt = now.AddDays(-30 + i * 2).AddHours(rnd.Next(0, 12));
            db.AuditLogs.AddRange(logs);

            // ---------- contact messages ----------
            db.ContactMessages.Add(new ContactMessage { Name = "Aylin K.", Email = "aylin@example.com", Topic = "feedback", Message = "Love the yellow everywhere. Could you add a dark mode some day?", Status = "new", CreatedAt = ago(1) });
            db.ContactMessages.Add(new ContactMessage { Name = "Tom Becker", Email = "tom@example.com", Topic = "collaboration", Message = "I run a small label in Berlin and would love to upload our catalog. Who can I talk to?", Status = "read", CreatedAt = ago(4) });
            db.ContactMessages.Add(new ContactMessage { Name = "Jin", Email = "jin@example.com", Topic = "bug", Message = "The player stops when my phone locks. Is that expected?", Status = "replied", CreatedAt = ago(9) });
            db.ContactMessages.Add(new ContactMessage { Name = "Sara", Email = "sara@example.com", Topic = "general", Message = "Is Wavelength going to stay free after the semester?", Status = "new", CreatedAt = ago(0.3) });

            db.SaveChanges();
        }
    }
}
