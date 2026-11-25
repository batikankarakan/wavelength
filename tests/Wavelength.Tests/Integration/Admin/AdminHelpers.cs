using Microsoft.EntityFrameworkCore;
using Wavelength.Tests.Infrastructure;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Integration.Admin;

internal static class AdminHelpers
{
    public static Task<int> TrackId(this WavelengthFactory app, string title) =>
        app.Db(db => db.Tracks.Where(t => t.Title == title).Select(t => t.Id).SingleAsync());

    public static Task<int> UserId(this WavelengthFactory app, string username) =>
        app.Db(db => db.Users.Where(u => u.Username == username).Select(u => u.Id).SingleAsync());

    public static Task<Track> Track(this WavelengthFactory app, int id) =>
        app.Db(db => db.Tracks.AsNoTracking().SingleAsync(t => t.Id == id));

    public static Task<List<Notification>> Notes(this WavelengthFactory app, int userId, string type) =>
        app.Db(db => db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.Type == type).OrderBy(n => n.Id).ToListAsync());

    public static Task<List<AuditLog>> Audit(this WavelengthFactory app, string action) =>
        app.Db(db => db.AuditLogs.AsNoTracking().Where(a => a.Action == action).OrderBy(a => a.Id).ToListAsync());

    public static Task<int> AuditCount(this WavelengthFactory app) => app.Db(db => db.AuditLogs.CountAsync());

    /// <summary>Follows the redirect and returns the flash message shown on the next page.</summary>
    public static async Task<string> Flash(this Session s, Session.Page postResult)
    {
        var next = await s.Get(postResult.Location!);
        return next.Doc.QuerySelector("[role=status]")?.TextContent.Trim() ?? "";
    }

    public static Dictionary<string, string> F(params (string Key, string Value)[] kv) => kv.ToDictionary(x => x.Key, x => x.Value);
}
