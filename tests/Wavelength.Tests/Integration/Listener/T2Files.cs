using System.Net.Http.Headers;
using Wavelength.Tests.Infrastructure;

namespace Wavelength.Tests.Integration.Listener;

/// <summary>Multipart helpers and sample files for the listener and studio tests.</summary>
internal static class T2Files
{
    public static byte[] Mp3 => System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "test-assets", "neon-highway.mp3"));

    // 1x1 transparent PNG
    public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    public static MultipartFormDataContent Form(params (string Name, string Value)[] fields)
    {
        var c = new MultipartFormDataContent();
        foreach (var (n, v) in fields) c.Add(new StringContent(v), n);
        return c;
    }

    public static MultipartFormDataContent File(this MultipartFormDataContent c, string field, string fileName, string contentType, byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        c.Add(part, field, fileName);
        return c;
    }

}
