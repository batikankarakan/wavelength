using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Wavelength.Web.Data;
using Wavelength.Web.Services;

namespace Wavelength.Web.Models
{
    /// <summary>One track as shown in lists, cards and the player queue.</summary>
    public class TrackItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public int ArtistId { get; set; }
        public string Artist { get; set; }
        public bool Verified { get; set; }
        public string CoverUrl { get; set; }
        public string Genre { get; set; }
        public string GenreColor { get; set; }
        public int DurationSec { get; set; }
        public int Plays { get; set; }
        public string Status { get; set; }
        public int? AlbumId { get; set; }
        public string AlbumTitle { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool Liked { get; set; }
        public string Color { get { return GenreColor ?? Fmt.ColorFor(Id); } }
    }

    public static class TrackQueries
    {
        public static IQueryable<Track> Published(this IQueryable<Track> q)
        {
            return q.Where(t => t.Status == TrackStatus.Published);
        }

        public static IQueryable<TrackItem> ToItems(this IQueryable<Track> q)
        {
            return q.Select(t => new TrackItem
            {
                Id = t.Id,
                Title = t.Title,
                ArtistId = t.ArtistId,
                Artist = t.Artist.StageName,
                Verified = t.Artist.Verified,
                CoverUrl = t.CoverUrl,
                Genre = t.TrackGenres.OrderBy(g => g.GenreId).Select(g => g.Genre.Name).FirstOrDefault(),
                GenreColor = t.TrackGenres.OrderBy(g => g.GenreId).Select(g => g.Genre.Color).FirstOrDefault(),
                DurationSec = t.DurationSec,
                Plays = t.PlaysCount,
                Status = t.Status,
                AlbumId = t.AlbumId,
                AlbumTitle = t.Album != null ? t.Album.Title : null,
                CreatedAt = t.CreatedAt,
            });
        }

        /// <summary>Marks which of the given tracks the user liked.</summary>
        public static void MarkLiked(this IEnumerable<TrackItem> items, ISet<int> likedIds)
        {
            foreach (var i in items) i.Liked = likedIds.Contains(i.Id);
        }

        /// <summary>Loads the user's likes for the given tracks and marks them.</summary>
        public static void MarkLiked(this List<TrackItem> items, AppDbContext db, int? userId)
        {
            if (userId == null || items.Count == 0) return;
            var ids = items.Select(i => i.Id).ToList();
            var liked = new HashSet<int>(db.TrackLikes.Where(l => l.UserId == userId && ids.Contains(l.TrackId)).Select(l => l.TrackId));
            items.MarkLiked(liked);
        }
    }

    public class TrackListVm
    {
        public TrackListVm() { Tracks = new List<TrackItem>(); Numbered = true; Empty = "Nothing here yet."; }

        public List<TrackItem> Tracks { get; set; }
        public bool Numbered { get; set; }
        public bool ShowAlbum { get; set; }
        public string Empty { get; set; }
        /// <summary>Optional per-row action rendered on the right (e.g. a remove button).</summary>
        public Func<TrackItem, IHtmlString> Extra { get; set; }
    }

    public class CoverVm
    {
        public CoverVm() { Color = "#FFE500"; Label = ""; Size = "md"; }

        public string Url { get; set; }
        public string Color { get; set; }
        public string Label { get; set; }
        public string Size { get; set; } // xs, sm, md, lg, fill
        public bool Round { get; set; }
    }

    public class AvatarVm
    {
        public string Url { get; set; }
        public string Name { get; set; }
        public string Css { get; set; }
    }

    public class PagerVm
    {
        public int Page { get; set; }
        public int TotalPages { get; set; }
        public Func<int, string> Url { get; set; }
    }

    public class AlbumItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public int ArtistId { get; set; }
        public string CoverUrl { get; set; }
        public string Type { get; set; }
        public DateTime ReleaseDate { get; set; }
        public int TrackCount { get; set; }
    }

    public class PlaylistItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Owner { get; set; }
        public string OwnerUsername { get; set; }
        public string CoverUrl { get; set; }
        public bool IsPublic { get; set; }
        public int TrackCount { get; set; }
        public string Description { get; set; }
    }

    public class ArtistItem
    {
        public int Id { get; set; }
        public string StageName { get; set; }
        public string AvatarUrl { get; set; }
        public string Country { get; set; }
        public bool Verified { get; set; }
        public int Followers { get; set; }
        public int Plays { get; set; }
    }

    public static class CatalogQueries
    {
        public static IQueryable<AlbumItem> ToItems(this IQueryable<Album> q)
        {
            return q.Select(a => new AlbumItem
            {
                Id = a.Id, Title = a.Title, Artist = a.Artist.StageName, ArtistId = a.ArtistId, CoverUrl = a.CoverUrl,
                Type = a.Type, ReleaseDate = a.ReleaseDate, TrackCount = a.Tracks.Count(t => t.Status == TrackStatus.Published),
            });
        }

        public static IQueryable<PlaylistItem> ToItems(this IQueryable<Playlist> q)
        {
            return q.Select(p => new PlaylistItem
            {
                Id = p.Id, Title = p.Title, Owner = p.Owner.DisplayName, OwnerUsername = p.Owner.Username, CoverUrl = p.CoverUrl,
                IsPublic = p.IsPublic, TrackCount = p.PlaylistTracks.Count, Description = p.Description,
            });
        }

        public static IQueryable<ArtistItem> ToItems(this IQueryable<ArtistProfile> q)
        {
            return q.Select(a => new ArtistItem
            {
                Id = a.Id, StageName = a.StageName, AvatarUrl = a.User.AvatarUrl, Country = a.Country, Verified = a.Verified,
                Followers = a.User.Followers.Count,
                Plays = a.Tracks.Where(t => t.Status == TrackStatus.Published).Sum(t => (int?)t.PlaysCount) ?? 0,
            });
        }
    }
}

namespace Wavelength.Web.Models
{
    /// <summary>Colored side panel of the sign in / sign up pages.</summary>
    public class AuthAsideVm
    {
        public string Sticker { get; set; }
        public string Line1 { get; set; }
        public string Highlight { get; set; }
        public string Line3 { get; set; }
        public string Text { get; set; }
        public string Color { get; set; }
    }
}
