using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Wavelength.Web.Data;

namespace Wavelength.Tests.Infrastructure;

/// <summary>
/// Starts the real ASP.NET MVC 5 app with a fresh SQL Server database, which the app creates and seeds on its
/// first request. One app + database per test class; both are removed on dispose. Two hosts:
/// <list type="bullet">
/// <item><b>iisexpress</b> (default on Windows, no Docker): IIS Express serves Wavelength.Web (build the solution
/// first) and the database is created in LocalDB, (localdb)\MSSQLLocalDB.</item>
/// <item><b>docker</b> (default on macOS/Linux): the app runs in a container built from docker/Dockerfile (Mono + XSP)
/// against the SQL Server from docker-compose.yml on localhost:1433 (docker compose up -d sql).</item>
/// </list>
/// Override with WAVELENGTH_TEST_HOST=iisexpress|docker. WAVELENGTH_TEST_SQL overrides the docker-mode SQL Server.
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

    private Process? _iisExpress;

    public static readonly bool UseIisExpress =
        (Environment.GetEnvironmentVariable("WAVELENGTH_TEST_HOST") ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "iisexpress" : "docker"))
        .Equals("iisexpress", StringComparison.OrdinalIgnoreCase);

    private const string Sa = "User Id=sa;Password=Wavelength!2026;TrustServerCertificate=True;MultipleActiveResultSets=True";
    private const string LocalDb = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    /// <summary>Connection string the tests use to check the database.</summary>
    private string HostSql => UseIisExpress
        ? LocalDb + ";Database=" + DatabaseName
        : (Environment.GetEnvironmentVariable("WAVELENGTH_TEST_SQL") ?? "Server=localhost,1433") + ";" + Sa + ";Database=" + DatabaseName;

    /// <summary>Connection string handed to the app (WAVELENGTH_DB, read by AppDbContext).</summary>
    private string AppSql => UseIisExpress
        ? $@"Data Source=(localdb)\MSSQLLocalDB;Initial Catalog={DatabaseName};Integrated Security=True;MultipleActiveResultSets=True"
        : $"Data Source=host.docker.internal,1433;Initial Catalog={DatabaseName};User Id=sa;Password=Wavelength!2026;MultipleActiveResultSets=True";

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
        var port = FreePort();
        BaseUrl = $"http://localhost:{port}";
        if (UseIisExpress)
        {
            await IisExpressSlot.WaitAsync();
            _holdsSlot = true;
            StartIisExpress(port);
        }
        else await StartContainer(port);

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
            if (DateTime.UtcNow > until)
                throw new TimeoutException("The app did not start: " + (UseIisExpress ? "IIS Express on " + BaseUrl + (_iisExpress!.HasExited ? " exited with code " + _iisExpress.ExitCode : "") : await Docker($"logs --tail 30 {_container}")));
            await Task.Delay(1000);
        }
    }

    private async Task StartContainer(int port)
    {
        await BuildLock.WaitAsync();
        try
        {
            if (!_built) { await Docker($"build -q -t {Image} -f docker/Dockerfile ."); _built = true; }
        }
        finally { BuildLock.Release(); }
        _container = await Docker($"run -d --rm --add-host=host.docker.internal:host-gateway -p {port}:8080 -e \"WAVELENGTH_DB={AppSql}\" {Image}");
    }

    // ---------- IIS Express + LocalDB (Windows, no Docker) ----------

    private static readonly object UploadsLock = new();
    // one IIS Express at a time: several instances serving the same folder in parallel were unstable
    private static readonly SemaphoreSlim IisExpressSlot = new(1, 1);
    private bool _holdsSlot;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _iisLog = new();
    private static bool _uploadsCleanupRegistered;

    private void StartIisExpress(int port)
    {
        var web = Path.Combine(RepoRoot, "Wavelength.Web");
        if (!File.Exists(Path.Combine(web, "bin", "Wavelength.Web.dll")))
            throw new InvalidOperationException("Build the solution first (Visual Studio: Build > Build Solution, or msbuild Wavelength.sln): Wavelength.Web\\bin\\Wavelength.Web.dll is missing.");
        var exe = new[]
        {
            Environment.GetEnvironmentVariable("IISEXPRESS_PATH"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "IIS Express", "iisexpress.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "IIS Express", "iisexpress.exe"),
        }.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p))
            ?? throw new FileNotFoundException("iisexpress.exe not found (install Visual Studio's ASP.NET workload or set IISEXPRESS_PATH).");

        RegisterUploadsCleanup(Path.Combine(web, "uploads"));
        var psi = new ProcessStartInfo(exe, $"/path:\"{web}\" /port:{port} /clr:v4.0 /systray:false")
        {
            // stdin stays open (redirected, never written): IIS Express stops when its console input closes
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        psi.Environment["WAVELENGTH_DB"] = AppSql; // inherited by the app (AppDbContext.DefaultConnection)
        _iisExpress = Process.Start(psi)!;
        _iisExpress.OutputDataReceived += (_, e) => { if (e.Data != null) { _iisLog.Enqueue(e.Data); while (_iisLog.Count > 300) _iisLog.TryDequeue(out string? _dropped); } };
        _iisExpress.ErrorDataReceived += (_, e) => { if (e.Data != null) _iisLog.Enqueue("ERR " + e.Data); };
        _iisExpress.BeginOutputReadLine();
        _iisExpress.BeginErrorReadLine();
    }

    /// <summary>Files the tests upload land in Wavelength.Web\\uploads; they are deleted when the test run ends.</summary>
    private static void RegisterUploadsCleanup(string dir)
    {
        lock (UploadsLock)
        {
            if (_uploadsCleanupRegistered) return;
            _uploadsCleanupRegistered = true;
            var before = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToHashSet() : new HashSet<string>();
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    if (!before.Contains(f)) try { File.Delete(f); } catch { /* best effort */ }
            };
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_container != "") try { await Docker($"rm -f {_container}", 60); } catch { /* already gone */ }
        if (_iisExpress != null)
        {
            if (_iisExpress.HasExited)
            {
                // it stopped on its own: keep its output for diagnosis (uploaded with the CI test results)
                var dir = Path.Combine(RepoRoot, "tests", "Wavelength.Tests", "TestResults");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"iisexpress-{new Uri(BaseUrl).Port}.log"),
                    $"exit code {_iisExpress.ExitCode} at {_iisExpress.ExitTime:O}\n" + string.Join("\n", _iisLog));
                Console.WriteLine($"IIS Express on {BaseUrl} exited early with code {_iisExpress.ExitCode}");
            }
            try { _iisExpress.Kill(true); } catch { /* already gone */ }
            _iisExpress.Dispose();
        }
        if (_holdsSlot) { _holdsSlot = false; IisExpressSlot.Release(); }
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
