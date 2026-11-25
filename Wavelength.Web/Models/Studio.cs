using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web;
using Wavelength.Web.Data;

namespace Wavelength.Web.Models
{
    /// <summary>Header of every studio page (artist name, verified badge, active tab).</summary>
    public class StudioHeader
    {
        public int ArtistId { get; set; }
        public string StageName { get; set; }
        public bool Verified { get; set; }
        public string Active { get; set; }
    }

    public class DayCount
    {
        public DateTime Day { get; set; }
        public int Count { get; set; }
    }

    public class StudioComment
    {
        public int TrackId { get; set; }
        public string TrackTitle { get; set; }
        public string User { get; set; }
        public string Username { get; set; }
        public string AvatarUrl { get; set; }
        public string Body { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class StudioTopTrack
    {
        public TrackItem Track { get; set; }
        public int Likes { get; set; }
    }

    public class StudioDashboardVm
    {
        public StudioDashboardVm()
        {
            Header = new StudioHeader();
            Daily = new List<DayCount>();
            TopTracks = new List<StudioTopTrack>();
            Comments = new List<StudioComment>();
            StatusCounts = new Dictionary<string, int>();
        }

        public StudioHeader Header { get; set; }
        public int TotalPlays { get; set; }
        public int Plays30 { get; set; }
        public int Likes { get; set; }
        public int Followers { get; set; }
        public int ShareClicks { get; set; }
        public List<DayCount> Daily { get; set; }
        public List<StudioTopTrack> TopTracks { get; set; }
        public List<StudioComment> Comments { get; set; }
        public Dictionary<string, int> StatusCounts { get; set; }

        public int CountOf(string status)
        {
            int c;
            return StatusCounts.TryGetValue(status, out c) ? c : 0;
        }
    }

    public class StudioTrackRow
    {
        public StudioTrackRow() { Color = "#FFE500"; }

        public int Id { get; set; }
        public string Title { get; set; }
        public string Status { get; set; }
        public string CoverUrl { get; set; }
        public string Color { get; set; }
        public string Genre { get; set; }
        public string Album { get; set; }
        public int? AlbumId { get; set; }
        public int Plays { get; set; }
        public int Likes { get; set; }
        public int Comments { get; set; }
        public int DurationSec { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class StudioTracksVm
    {
        public StudioTracksVm()
        {
            Header = new StudioHeader();
            Filter = "all";
            Counts = new Dictionary<string, int>();
            Tracks = new List<StudioTrackRow>();
        }

        public StudioHeader Header { get; set; }
        public string Filter { get; set; }
        public Dictionary<string, int> Counts { get; set; }
        public List<StudioTrackRow> Tracks { get; set; }

        public int CountOf(string status)
        {
            int c;
            return Counts.TryGetValue(status, out c) ? c : 0;
        }
    }

    /// <summary>Upload / edit form. Fields are posted with the "Form." prefix.</summary>
    public class TrackForm
    {
        public TrackForm() { GenreIds = new List<int>(); }

        public int? Id { get; set; }

        [Required(ErrorMessage = "Give your track a title."), StringLength(120, ErrorMessage = "Titles can be at most 120 characters.")]
        public string Title { get; set; }

        [StringLength(8000, ErrorMessage = "Lyrics can be at most 8000 characters."), DataType(DataType.MultilineText)]
        public string Lyrics { get; set; }

        public List<int> GenreIds { get; set; }

        [Display(Name = "Album")]
        public int? AlbumId { get; set; }

        public int DurationSec { get; set; }
        public HttpPostedFileBase Audio { get; set; }
        public HttpPostedFileBase Cover { get; set; }
        public bool RemoveCover { get; set; }
    }

    public class AlbumOption
    {
        public int Id { get; set; }
        public string Title { get; set; }
    }

    public class TrackFormVm
    {
        public TrackFormVm()
        {
            Header = new StudioHeader();
            Form = new TrackForm();
            Status = TrackStatus.Draft;
            Genres = new List<Genre>();
            Albums = new List<AlbumOption>();
        }

        public StudioHeader Header { get; set; }
        public TrackForm Form { get; set; }
        public bool IsNew { get { return Form.Id == null; } }
        public string Status { get; set; }
        public string AudioUrl { get; set; }
        public string CoverUrl { get; set; }
        public string RejectionReason { get; set; }
        public List<Genre> Genres { get; set; }
        public List<AlbumOption> Albums { get; set; }
    }

    public class AlbumForm
    {
        public AlbumForm() { Type = "album"; ReleaseDate = DateTime.UtcNow.Date; }

        public int? Id { get; set; }

        [Required(ErrorMessage = "Give the album a title."), StringLength(100, ErrorMessage = "Titles can be at most 100 characters.")]
        public string Title { get; set; }

        [Required(ErrorMessage = "Choose single, EP or album.")]
        public string Type { get; set; }

        [Display(Name = "Release date"), DataType(DataType.Date), DisplayFormat(DataFormatString = "{0:yyyy-MM-dd}", ApplyFormatInEditMode = true)]
        public DateTime ReleaseDate { get; set; }

        public HttpPostedFileBase Cover { get; set; }
    }

    public class StudioAlbumRow
    {
        public StudioAlbumRow() { Tracks = new List<StudioTrackRow>(); }

        public int Id { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public DateTime ReleaseDate { get; set; }
        public string CoverUrl { get; set; }
        public List<StudioTrackRow> Tracks { get; set; }
    }

    public class StudioAlbumsVm
    {
        public StudioAlbumsVm()
        {
            Header = new StudioHeader();
            Albums = new List<StudioAlbumRow>();
            Singles = new List<StudioTrackRow>();
            NewAlbum = new AlbumForm();
        }

        public StudioHeader Header { get; set; }
        public List<StudioAlbumRow> Albums { get; set; }
        public List<StudioTrackRow> Singles { get; set; }
        /// <summary>The "new album" form; posted with the "NewAlbum." prefix.</summary>
        public AlbumForm NewAlbum { get; set; }
    }

    public class ArtistProfileForm
    {
        [Required(ErrorMessage = "Stage name is required."), StringLength(80, ErrorMessage = "Stage names can be at most 80 characters.")]
        [Display(Name = "Stage name")]
        public string StageName { get; set; }

        [StringLength(60, ErrorMessage = "Country can be at most 60 characters.")]
        public string Country { get; set; }

        [StringLength(150, ErrorMessage = "Website can be at most 150 characters."), Url(ErrorMessage = "Enter a full URL, like https://example.com.")]
        public string Website { get; set; }

        public HttpPostedFileBase Banner { get; set; }
        public bool RemoveBanner { get; set; }
    }

    public class StudioProfileVm
    {
        public StudioProfileVm() { Header = new StudioHeader(); Form = new ArtistProfileForm(); }

        public StudioHeader Header { get; set; }
        /// <summary>Posted with the "Form." prefix.</summary>
        public ArtistProfileForm Form { get; set; }
        public string BannerUrl { get; set; }
        public string AvatarUrl { get; set; }
        public int Followers { get; set; }
    }
}
