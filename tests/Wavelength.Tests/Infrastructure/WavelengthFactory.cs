using Wavelength.Tests.Infrastructure;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Infrastructure;

/// <summary>
/// Starts the real ASP.NET MVC 5 app (Docker image built from docker/Dockerfile: Mono + XSP) with a fresh
/// SQL Server database, which the app creates and seeds on its first request. One app + database per test class;
/// both are removed on dispose. Needs Docker and the SQL Server from docker-compose.yml on localhost:1433
/// (docker compose up -d sql). Set WAVELENGTH_TEST_SQL to use another SQL Server for the test-side checks.
/// </summary>
public class WavelengthFactory : IAsyncLifetime
{
    /// <summary>Docker image tag for the app under test (WAVELENGTH_TEST_IMAGE keeps parallel runs apart).</summary>
    public static readonly string Image = Environment.GetEnvironmentVariable("WAVELENGTH_TEST_IMAGE") ?? "wavelength-test";
    private static readonly SemaphoreSlim BuildLock = new(1, 1);
    private static bool _built;

    public string DatabaseName { get; } = $"Wavelength_T_{Guid.NewGuid():N}"[..26];
    public string BaseUrl { get; private set; } = "";
    private string _container = "";

    private const string Sa = "User Id=sa;Password=Wavelength!2026;TrustServerCertificate=True;MultipleActiveResultSets=True";
    private string HostSql => (Environment.GetEnvironmentVariable("WAVELENGTH_TEST_SQL") ?? "Server=localhost,1433") + ";" + Sa + ";Database=" + DatabaseName;
    private string AppSql => $"Data Source=host.docker.internal,1433;Initial Catalog={DatabaseName};User Id=sa;Password=Wavelength!2026;MultipleActiveResultSets=True";

    public static string RepoRoot
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "Wavelength.sln"))) return d.FullName;
            throw new DirectoryNotFoundException("Wavelength.sln not found above " + AppContext.BaseDirectory);
        }
    }

    private static async Task<string> Docker(string args, int timeoutSec = 900)
    {
        var psi = new ProcessStartInfo("docker", args) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = RepoRoot };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        await p.WaitForExitAsync(cts.Token);
        if (p.ExitCode != 0) throw new InvalidOperationException($"docker {args.Split(' ')[0]} failed: {await stderr}");
        return (await stdout).Trim();
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public async Task InitializeAsync()
    {
        await BuildLock.WaitAsync();
        try
        {
            if (!_built) { await Docker($"build -q -t {Image} -f docker/Dockerfile ."); _built = true; }
        }
        finally { BuildLock.Release(); }

        var port = FreePort();
        BaseUrl = $"http://localhost:{port}";
        _container = await Docker($"run -d --rm --add-host=host.docker.internal:host-gateway -p {port}:8080 -e \"WAVELENGTH_DB={AppSql}\" {Image}");

        // the first request creates and seeds the database
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var until = DateTime.UtcNow.AddMinutes(4);
        while (true)
        {
            try
            {
                var res = await http.GetAsync(BaseUrl + "/terms");
                if (res.StatusCode == HttpStatusCode.OK) break;
            }
            catch (Exception) when (DateTime.UtcNow < until) { }
            if (DateTime.UtcNow > until) throw new TimeoutException("The app did not start: " + await Docker($"logs --tail 30 {_container}"));
            await Task.Delay(1000);
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_container != "") try { await Docker($"rm -f {_container}", 60); } catch { /* already gone */ }
        try
        {
            await using var db = NewDb();
            await db.Database.ExecuteSqlRawAsync($"IF DB_ID('{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END");
        }
        catch { /* best effort */ }
    }

    public Session NewSession() => new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() })
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(120),
    });

    public async Task<Session> LoginAs(string email, string password = Seeder.DemoPassword)
    {
        var s = NewSession();
        await s.Login(email, password);
        return s;
    }

    /// <summary>A database context for test-side checks (same tables as the app, mapped with EF Core).</summary>
    public AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(HostSql.Replace(";Database=" + DatabaseName, ";Database=master")).UseSnakeCaseNamingConvention().Options);

    public async Task<T> Db<T>(Func<AppDbContext, Task<T>> work)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(HostSql).UseSnakeCaseNamingConvention().Options);
        return await work(db);
    }

    public Task Db(Func<AppDbContext, Task> work) => Db(async db => { await work(db); return 0; });

    /// <summary>Whether a file (e.g. an uploaded "/uploads/..." url) is served by the app.</summary>
    public async Task<bool> FileExists(string url)
    {
        using var http = new HttpClient();
        var res = await http.GetAsync(BaseUrl + url);
        return res.StatusCode == HttpStatusCode.OK;
    }
}

/// <summary>Demo password of the seeded accounts (WavelengthInitializer.DemoPassword in the app).</summary>
public static class Seeder
{
    public const string DemoPassword = "Wavelength123!";
}
