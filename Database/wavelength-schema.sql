/*
  Wavelength - SQL Server schema (19 tables, analysis report section 2)

  You do NOT need to run this script: the web application creates the database (LocalDB, App_Data\Wavelength.mdf)
  with these tables, keys, indexes and CHECK constraints automatically on first start (Entity Framework 6,
  Data/AppDbContext.cs + Data/WavelengthInitializer.cs) and fills it with demo data.
  The script is here as a readable reference of the database design and for creating the schema by hand.
*/

﻿CREATE TABLE [contact_messages] (
    [id] int NOT NULL IDENTITY,
    [name] nvarchar(80) NOT NULL,
    [email] nvarchar(120) NOT NULL,
    [topic] nvarchar(30) NOT NULL,
    [message] nvarchar(max) NOT NULL,
    [status] nvarchar(10) NOT NULL,
    [created_at] datetime NOT NULL,
    CONSTRAINT [pk_contact_messages] PRIMARY KEY ([id]),
    CONSTRAINT [ck_contact_status] CHECK (status IN ('new','read','replied'))
);
GO

CREATE TABLE [genres] (
    [id] int NOT NULL IDENTITY,
    [name] nvarchar(40) NOT NULL,
    [slug] nvarchar(40) NOT NULL,
    [color] nvarchar(7) NOT NULL,
    CONSTRAINT [pk_genres] PRIMARY KEY ([id])
);
GO

CREATE TABLE [users] (
    [id] int NOT NULL IDENTITY,
    [username] nvarchar(30) NOT NULL,
    [email] nvarchar(120) NOT NULL,
    [password_hash] nvarchar(255) NOT NULL,
    [display_name] nvarchar(60) NOT NULL,
    [avatar_url] nvarchar(255) NULL,
    [bio] nvarchar(300) NULL,
    [role] nvarchar(20) NOT NULL,
    [is_active] bit NOT NULL,
    [created_at] datetime NOT NULL,
    CONSTRAINT [pk_users] PRIMARY KEY ([id]),
    CONSTRAINT [ck_users_role] CHECK (role IN ('listener','artist','moderator','admin'))
);
GO

CREATE TABLE [artist_profiles] (
    [id] int NOT NULL IDENTITY,
    [user_id] int NOT NULL,
    [stage_name] nvarchar(80) NOT NULL,
    [verified] bit NOT NULL,
    [banner_url] nvarchar(255) NULL,
    [country] nvarchar(60) NULL,
    [website] nvarchar(150) NULL,
    CONSTRAINT [pk_artist_profiles] PRIMARY KEY ([id]),
    CONSTRAINT [fk_artist_profiles_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [audit_logs] (
    [id] int NOT NULL IDENTITY,
    [admin_id] int NOT NULL,
    [action] nvarchar(60) NOT NULL,
    [target] nvarchar(120) NOT NULL,
    [created_at] datetime NOT NULL,
    CONSTRAINT [pk_audit_logs] PRIMARY KEY ([id]),
    CONSTRAINT [fk_audit_logs_users_admin_id] FOREIGN KEY ([admin_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [follows] (
    [follower_id] int NOT NULL,
    [followed_id] int NOT NULL,
    [created_at] datetime NOT NULL,
    CONSTRAINT [pk_follows] PRIMARY KEY ([follower_id], [followed_id]),
    CONSTRAINT [fk_follows_users_followed_id] FOREIGN KEY ([followed_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_follows_users_follower_id] FOREIGN KEY ([follower_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [notifications] (
    [id] int NOT NULL IDENTITY,
    [user_id] int NOT NULL,
    [type] nvarchar(30) NOT NULL,
    [payload] nvarchar(500) NOT NULL,
    [is_read] bit NOT NULL,
    [created_at] datetime NOT NULL,
    CONSTRAINT [pk_notifications] PRIMARY KEY ([id]),
    CONSTRAINT [fk_notifications_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [playlists] (
    [id] int NOT NULL IDENTITY,
    [owner_id] int NOT NULL,
    [title] nvarchar(100) NOT NULL,
    [description] nvarchar(300) NULL,
    [cover_url] nvarchar(255) NULL,
    [is_public] bit NOT NULL,
    [created_at] datetime NOT NULL,
    CONSTRAINT [pk_playlists] PRIMARY KEY ([id]),
    CONSTRAINT [fk_playlists_users_owner_id] FOREIGN KEY ([owner_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [albums] (
    [id] int NOT NULL IDENTITY,
    [artist_id] int NOT NULL,
    [title] nvarchar(100) NOT NULL,
    [cover_url] nvarchar(255) NULL,
    [release_date] date NOT NULL,
    [type] nvarchar(10) NOT NULL,
    CONSTRAINT [pk_albums] PRIMARY KEY ([id]),
    CONSTRAINT [ck_albums_type] CHECK (type IN ('single','ep','album')),
    CONSTRAINT [fk_albums_artist_profiles_artist_id] FOREIGN KEY ([artist_id]) REFERENCES [artist_profiles] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [saved_playlists] (
    [user_id] int NOT NULL,
    [playlist_id] int NOT NULL,
    [saved_at] datetime NOT NULL,
    CONSTRAINT [pk_saved_playlists] PRIMARY KEY ([user_id], [playlist_id]),
    CONSTRAINT [fk_saved_playlists_playlists_playlist_id] FOREIGN KEY ([playlist_id]) REFERENCES [playlists] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_saved_playlists_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [saved_albums] (
    [user_id] int NOT NULL,
    [album_id] int NOT NULL,
    [saved_at] datetime NOT NULL,
    CONSTRAINT [pk_saved_albums] PRIMARY KEY ([user_id], [album_id]),
    CONSTRAINT [fk_saved_albums_albums_album_id] FOREIGN KEY ([album_id]) REFERENCES [albums] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_saved_albums_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [tracks] (
    [id] int NOT NULL IDENTITY,
    [artist_id] int NOT NULL,
    [album_id] int NULL,
    [title] nvarchar(120) NOT NULL,
    [audio_url] nvarchar(255) NOT NULL,
    [cover_url] nvarchar(255) NULL,
    [duration_sec] int NOT NULL,
    [lyrics] nvarchar(max) NULL,
    [status] nvarchar(12) NOT NULL,
    [plays_count] int NOT NULL,
    [created_at] datetime NOT NULL,
    CONSTRAINT [pk_tracks] PRIMARY KEY ([id]),
    CONSTRAINT [ck_tracks_status] CHECK (status IN ('draft','pending','published','rejected','hidden')),
    CONSTRAINT [fk_tracks_albums_album_id] FOREIGN KEY ([album_id]) REFERENCES [albums] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_tracks_artist_profiles_artist_id] FOREIGN KEY ([artist_id]) REFERENCES [artist_profiles] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [comments] (
    [id] int NOT NULL IDENTITY,
    [user_id] int NOT NULL,
    [track_id] int NOT NULL,
    [parent_id] int NULL,
    [body] nvarchar(max) NOT NULL,
    [is_hidden] bit NOT NULL,
    [created_at] datetime NOT NULL,
    CONSTRAINT [pk_comments] PRIMARY KEY ([id]),
    CONSTRAINT [fk_comments_comments_parent_id] FOREIGN KEY ([parent_id]) REFERENCES [comments] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_comments_tracks_track_id] FOREIGN KEY ([track_id]) REFERENCES [tracks] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_comments_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [listening_history] (
    [id] int NOT NULL IDENTITY,
    [user_id] int NOT NULL,
    [track_id] int NOT NULL,
    [played_at] datetime NOT NULL,
    [seconds_listened] int NOT NULL,
    CONSTRAINT [pk_listening_history] PRIMARY KEY ([id]),
    CONSTRAINT [fk_listening_history_tracks_track_id] FOREIGN KEY ([track_id]) REFERENCES [tracks] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_listening_history_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [playlist_tracks] (
    [playlist_id] int NOT NULL,
    [track_id] int NOT NULL,
    [position] int NOT NULL,
    [added_at] datetime NOT NULL,
    CONSTRAINT [pk_playlist_tracks] PRIMARY KEY ([playlist_id], [track_id]),
    CONSTRAINT [fk_playlist_tracks_playlists_playlist_id] FOREIGN KEY ([playlist_id]) REFERENCES [playlists] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_playlist_tracks_tracks_track_id] FOREIGN KEY ([track_id]) REFERENCES [tracks] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [share_links] (
    [id] int NOT NULL IDENTITY,
    [track_id] int NOT NULL,
    [created_by] int NOT NULL,
    [token] nvarchar(32) NOT NULL,
    [click_count] int NOT NULL,
    [created_at] datetime NOT NULL,
    CONSTRAINT [pk_share_links] PRIMARY KEY ([id]),
    CONSTRAINT [fk_share_links_tracks_track_id] FOREIGN KEY ([track_id]) REFERENCES [tracks] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_share_links_users_created_by] FOREIGN KEY ([created_by]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [track_genres] (
    [track_id] int NOT NULL,
    [genre_id] int NOT NULL,
    CONSTRAINT [pk_track_genres] PRIMARY KEY ([track_id], [genre_id]),
    CONSTRAINT [fk_track_genres_genres_genre_id] FOREIGN KEY ([genre_id]) REFERENCES [genres] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_track_genres_tracks_track_id] FOREIGN KEY ([track_id]) REFERENCES [tracks] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [track_likes] (
    [user_id] int NOT NULL,
    [track_id] int NOT NULL,
    [created_at] datetime NOT NULL,
    CONSTRAINT [pk_track_likes] PRIMARY KEY ([user_id], [track_id]),
    CONSTRAINT [fk_track_likes_tracks_track_id] FOREIGN KEY ([track_id]) REFERENCES [tracks] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_track_likes_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [reports] (
    [id] int NOT NULL IDENTITY,
    [reporter_id] int NOT NULL,
    [track_id] int NULL,
    [comment_id] int NULL,
    [reported_user_id] int NULL,
    [reason] nvarchar(300) NOT NULL,
    [status] nvarchar(12) NOT NULL,
    [handled_by] int NULL,
    CONSTRAINT [pk_reports] PRIMARY KEY ([id]),
    CONSTRAINT [ck_reports_one_target] CHECK ((CASE WHEN track_id IS NULL THEN 0 ELSE 1 END + CASE WHEN comment_id IS NULL THEN 0 ELSE 1 END + CASE WHEN reported_user_id IS NULL THEN 0 ELSE 1 END) = 1),
    CONSTRAINT [ck_reports_status] CHECK (status IN ('open','resolved','dismissed')),
    CONSTRAINT [fk_reports_comments_comment_id] FOREIGN KEY ([comment_id]) REFERENCES [comments] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_reports_tracks_track_id] FOREIGN KEY ([track_id]) REFERENCES [tracks] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_reports_users_handled_by] FOREIGN KEY ([handled_by]) REFERENCES [users] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_reports_users_reported_user_id] FOREIGN KEY ([reported_user_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION,
    CONSTRAINT [fk_reports_users_reporter_id] FOREIGN KEY ([reporter_id]) REFERENCES [users] ([id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [ix_albums_artist_id] ON [albums] ([artist_id]);
GO

CREATE UNIQUE INDEX [ix_artist_profiles_user_id] ON [artist_profiles] ([user_id]);
GO

CREATE INDEX [ix_audit_logs_admin_id] ON [audit_logs] ([admin_id]);
GO

CREATE INDEX [ix_comments_parent_id] ON [comments] ([parent_id]);
GO

CREATE INDEX [ix_comments_track_id] ON [comments] ([track_id]);
GO

CREATE INDEX [ix_comments_user_id] ON [comments] ([user_id]);
GO

CREATE INDEX [ix_follows_followed_id] ON [follows] ([followed_id]);
GO

CREATE UNIQUE INDEX [ix_genres_name] ON [genres] ([name]);
GO

CREATE UNIQUE INDEX [ix_genres_slug] ON [genres] ([slug]);
GO

CREATE INDEX [ix_listening_history_played_at] ON [listening_history] ([played_at]);
GO

CREATE INDEX [ix_listening_history_track_id] ON [listening_history] ([track_id]);
GO

CREATE INDEX [ix_listening_history_user_id] ON [listening_history] ([user_id]);
GO

CREATE INDEX [ix_notifications_user_id_is_read] ON [notifications] ([user_id], [is_read]);
GO

CREATE INDEX [ix_playlist_tracks_track_id] ON [playlist_tracks] ([track_id]);
GO

CREATE INDEX [ix_playlists_owner_id] ON [playlists] ([owner_id]);
GO

CREATE INDEX [ix_reports_comment_id] ON [reports] ([comment_id]);
GO

CREATE INDEX [ix_reports_handled_by] ON [reports] ([handled_by]);
GO

CREATE INDEX [ix_reports_reported_user_id] ON [reports] ([reported_user_id]);
GO

CREATE INDEX [ix_reports_reporter_id] ON [reports] ([reporter_id]);
GO

CREATE INDEX [ix_reports_track_id] ON [reports] ([track_id]);
GO

CREATE INDEX [ix_saved_albums_album_id] ON [saved_albums] ([album_id]);
GO

CREATE INDEX [ix_saved_playlists_playlist_id] ON [saved_playlists] ([playlist_id]);
GO

CREATE INDEX [ix_share_links_created_by] ON [share_links] ([created_by]);
GO

CREATE UNIQUE INDEX [ix_share_links_token] ON [share_links] ([token]);
GO

CREATE INDEX [ix_share_links_track_id] ON [share_links] ([track_id]);
GO

CREATE INDEX [ix_track_genres_genre_id] ON [track_genres] ([genre_id]);
GO

CREATE INDEX [ix_track_likes_track_id] ON [track_likes] ([track_id]);
GO

CREATE INDEX [ix_tracks_album_id] ON [tracks] ([album_id]);
GO

CREATE INDEX [ix_tracks_artist_id] ON [tracks] ([artist_id]);
GO

CREATE INDEX [ix_tracks_status] ON [tracks] ([status]);
GO

CREATE UNIQUE INDEX [ix_users_email] ON [users] ([email]);
GO

CREATE UNIQUE INDEX [ix_users_username] ON [users] ([username]);
GO
