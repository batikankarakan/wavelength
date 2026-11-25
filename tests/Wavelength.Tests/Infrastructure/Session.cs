using System.Net;
using System.Net.Http.Json;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Wavelength.Tests.Infrastructure;

/// <summary>A browser-like client: keeps cookies, reads antiforgery tokens from pages, posts forms.</summary>
public class Session
{
    public HttpClient Client { get; }
    private static readonly HtmlParser Parser = new();
    private string? _token;

    public Session(HttpClient client) => Client = client;

    public record Page(HttpStatusCode Status, IHtmlDocument Doc, string Html, HttpResponseMessage Response)
    {
        /// <summary>Redirect target as a local path ("/login?ReturnUrl=..."), also when the server sent an absolute URL.</summary>
        public string? Location
        {
            get
            {
                var l = Response.Headers.Location;
                if (l == null) return null;
                return l.IsAbsoluteUri && l.Host == "localhost" ? l.PathAndQuery + l.Fragment : l.ToString();
            }
        }
        public string Text => Doc.Body?.TextContent ?? "";
    }

    public async Task<Page> Get(string url)
    {
        var res = await Client.GetAsync(url);
        var html = await res.Content.ReadAsStringAsync();
        var doc = Parser.ParseDocument(html);
        // the layout writes @Html.AntiForgeryToken() into #csrf (sent as X-CSRF-TOKEN / form field)
        var t = (doc.QuerySelector("#csrf input[name=__RequestVerificationToken]") ?? doc.QuerySelector("input[name=__RequestVerificationToken]"))?.GetAttribute("value");
        if (!string.IsNullOrEmpty(t)) _token = t;
        return new Page(res.StatusCode, doc, html, res);
    }

    /// <summary>Antiforgery token for the current cookie (loads a page if needed).</summary>
    public async Task<string> Token()
    {
        if (_token == null) await Get("/terms");
        return _token!;
    }

    public async Task<Page> Post(string url, IEnumerable<KeyValuePair<string, string>>? fields = null, bool withToken = true)
    {
        var list = (fields ?? Array.Empty<KeyValuePair<string, string>>()).ToList();
        if (withToken) list.Add(new("__RequestVerificationToken", await Token()));
        var res = await Client.PostAsync(url, new FormUrlEncodedContent(list));
        var html = await res.Content.ReadAsStringAsync();
        return new Page(res.StatusCode, Parser.ParseDocument(html), html, res);
    }

    public Task<Page> Post(string url, object fields) => Post(url, Fields(fields));

    public async Task<Page> PostMultipart(string url, MultipartFormDataContent content)
    {
        content.Add(new StringContent(await Token()), "__RequestVerificationToken");
        var res = await Client.PostAsync(url, content);
        var html = await res.Content.ReadAsStringAsync();
        return new Page(res.StatusCode, Parser.ParseDocument(html), html, res);
    }

    public async Task<HttpResponseMessage> PostJson(string url, object? body = null)
    {
        // a sized body (like a browser's fetch); JsonContent would stream it chunked, which XSP does not read
        var json = System.Text.Json.JsonSerializer.Serialize(body ?? new { }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        req.Headers.Add("X-CSRF-TOKEN", await Token());
        return await Client.SendAsync(req);
    }

    public async Task Login(string email, string password)
    {
        await Get("/login");
        var res = await Post("/login", new Dictionary<string, string> { ["Email"] = email, ["Password"] = password });
        if (res.Status != HttpStatusCode.Redirect) throw new InvalidOperationException($"Login failed for {email}: {res.Status}");
        _token = null; // token is bound to the user after login
    }

    /// <summary>Turns an anonymous object into form fields (arrays become repeated keys).</summary>
    public static List<KeyValuePair<string, string>> Fields(object o)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (o is IEnumerable<KeyValuePair<string, string>> kv) { list.AddRange(kv); return list; }
        foreach (var p in o.GetType().GetProperties())
        {
            var v = p.GetValue(o);
            if (v is string s) list.Add(new(p.Name, s));
            else if (v is System.Collections.IEnumerable e) foreach (var x in e) list.Add(new(p.Name, x?.ToString() ?? ""));
            else if (v is bool b) list.Add(new(p.Name, b ? "true" : "false"));
            else if (v != null) list.Add(new(p.Name, v.ToString()!));
        }
        return list;
    }
}
