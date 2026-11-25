using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Wavelength.Web.Data;

namespace Wavelength.Web.Models
{
    // View models for the listener pages (report section 3.2).

    /// <summary>Header of the listener pages (sticker, big title, tabs).</summary>
    public class LibraryHeroVm
    {
        public string Active { get; set; }
        public string Sticker { get; set; }
        public string StickerColor { get; set; }
        public string Title { get; set; }
        public string Highlight { get; set; }
        public string Text { get; set; }
    }

    public class LibraryVm
    {
        public LibraryVm()
        {
            Tab = "liked";
            Liked = new List<TrackItem>();
            Albums = new List<AlbumItem>();
            Playlists = new List<PlaylistItem>();
        }

        public string Tab { get; set; }
        public List<TrackItem> Liked { get; set; }
        public List<AlbumItem> Albums { get; set; }
        public List<PlaylistItem> Playlists { get; set; }
        public int LikedCount { get; set; }
        public int AlbumCount { get; set; }
        public int PlaylistCount { get; set; }
    }

    public class MyPlaylistsVm
    {
        public MyPlaylistsVm() { Playlists = new List<PlaylistItem>(); }
        public List<PlaylistItem> Playlists { get; set; }
    }

    public class HistoryEntry
    {
        public int Id { get; set; }
        public DateTime PlayedAt { get; set; }
        public int SecondsListened { get; set; }
        public TrackItem Track { get; set; }
    }

    public class HistoryDay
    {
        public DateTime Day { get; set; }
        public List<HistoryEntry> Entries { get; set; }
    }

    public class HistoryVm
    {
        public HistoryVm()
        {
            Range = "all";
            Genres = new List<Genre>();
            Days = new List<HistoryDay>();
            Page = 1;
            TotalPages = 1;
        }

        public string Range { get; set; }
        public string Genre { get; set; }
        public List<Genre> Genres { get; set; }
        public List<HistoryDay> Days { get; set; }
        public int TotalPlays { get; set; }
        public int TotalMinutes { get; set; }
        public int UniqueTracks { get; set; }
        public int Page { get; set; }
        public int TotalPages { get; set; }
    }

    public class FollowPerson
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string AvatarUrl { get; set; }
        public int? ArtistId { get; set; }
        public string StageName { get; set; }
        public bool Verified { get; set; }
        public int Followers { get; set; }
        public DateTime Since { get; set; }
        public bool IFollow { get; set; }
        public string Name { get { return StageName ?? DisplayName; } }
        public string Link { get { return ArtistId != null ? "/artists/" + ArtistId : "/users/" + Username; } }
    }

    public class FollowingVm
    {
        public FollowingVm() { Tab = "following"; People = new List<FollowPerson>(); }

        public string Tab { get; set; }
        public List<FollowPerson> People { get; set; }
        public int FollowingCount { get; set; }
        public int FollowersCount { get; set; }
    }

    public class NotificationRow
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public string Text { get; set; }
        public string Link { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class NotificationsVm
    {
        public NotificationsVm() { Items = new List<NotificationRow>(); Page = 1; TotalPages = 1; }

        public bool UnreadOnly { get; set; }
        public List<NotificationRow> Items { get; set; }
        public int Unread { get; set; }
        public int Page { get; set; }
        public int TotalPages { get; set; }
    }

    public class PlaylistForm
    {
        public PlaylistForm() { Title = ""; IsPublic = true; TrackIds = new List<int>(); }

        public int? Id { get; set; }

        [Required(ErrorMessage = "Give your playlist a title."), StringLength(100, ErrorMessage = "Title can be at most 100 characters.")]
        public string Title { get; set; }

        [StringLength(300, ErrorMessage = "Description can be at most 300 characters.")]
        public string Description { get; set; }

        public bool IsPublic { get; set; }
        public string CoverUrl { get; set; }
        public bool RemoveCover { get; set; }
        public List<int> TrackIds { get; set; }
    }

    /// <summary>Track shown in the playlist editor (passed to the Vue island as JSON).</summary>
    public class EditorTrack
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string CoverUrl { get; set; }
        public string Color { get; set; }
        public int DurationSec { get; set; }
    }

    public class PlaylistEditVm
    {
        public PlaylistEditVm() { Form = new PlaylistForm(); Tracks = new List<EditorTrack>(); }

        public PlaylistForm Form { get; set; }
        public List<EditorTrack> Tracks { get; set; }
    }

    public class ProfileForm
    {
        [Required(ErrorMessage = "Please tell us your name."), StringLength(60, MinimumLength = 2, ErrorMessage = "Name must be 2-60 characters.")]
        [Display(Name = "Display name")]
        public string DisplayName { get; set; }

        [Required(ErrorMessage = "Pick a username."), StringLength(30, MinimumLength = 3, ErrorMessage = "Username must be 3-30 characters.")]
        [RegularExpression("^[a-zA-Z0-9_.]+$", ErrorMessage = "Use letters, numbers, dots and underscores only.")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Enter your email."), EmailAddress(ErrorMessage = "Enter a valid email address."), StringLength(120)]
        public string Email { get; set; }

        [StringLength(300, ErrorMessage = "Bio can be at most 300 characters.")]
        public string Bio { get; set; }

        public bool RemoveAvatar { get; set; }
    }

    public class PasswordForm
    {
        [Required(ErrorMessage = "Enter your current password."), DataType(DataType.Password), Display(Name = "Current")]
        public string CurrentPassword { get; set; }

        [Required(ErrorMessage = "Choose a new password."), StringLength(100, MinimumLength = 8, ErrorMessage = "Password needs at least 8 characters.")]
        [DataType(DataType.Password), Display(Name = "New")]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "Repeat the new password."), System.ComponentModel.DataAnnotations.Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password), Display(Name = "Repeat new")]
        public string ConfirmPassword { get; set; }
    }

    public class SettingsVm
    {
        public SettingsVm() { Profile = new ProfileForm(); Password = new PasswordForm(); Section = "profile"; Role = ""; }

        public ProfileForm Profile { get; set; }
        public PasswordForm Password { get; set; }
        public string AvatarUrl { get; set; }
        public string Role { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Section { get; set; }
    }
}
