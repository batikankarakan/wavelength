// Turns the .cshtml views into C# (the same way ASP.NET MVC 5 does at runtime) so they can be compiled
// on any OS by tools/compile-check. Usage: dotnet run --project tools/razor-gen -- <webProjectDir> <outDir>
using System.CodeDom.Compiler;
using System.Text.RegularExpressions;
using System.Web.Razor;
using System.Web.Razor.Generator;
using Microsoft.CSharp;

var web = Path.GetFullPath(args[0]);
var outDir = Path.GetFullPath(args[1]);
if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
Directory.CreateDirectory(outDir);

var namespaces = new[]
{
    "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net", "System.Web", "System.Web.Helpers",
    "System.Web.Security", "System.Web.UI", "System.Web.WebPages", "System.Web.Mvc", "System.Web.Mvc.Ajax", "System.Web.Mvc.Html",
    "System.Web.Routing", "Wavelength.Web", "Wavelength.Web.Data", "Wavelength.Web.Models", "Wavelength.Web.Services", "Wavelength.Web.Infrastructure",
};
var provider = new CSharpCodeProvider();
int n = 0, failed = 0;

foreach (var file in Directory.EnumerateFiles(web, "*.cshtml", SearchOption.AllDirectories))
{
    var rel = Path.GetRelativePath(web, file).Replace('\\', '/');
    if (rel.StartsWith("bin/") || rel.StartsWith("obj/") || rel.StartsWith("node_modules/")) continue;
    var isAppCode = rel.StartsWith("App_Code/");
    var source = File.ReadAllText(file);

    var lines = source.Split('\n');
    string model = "dynamic";
    for (var i = 0; i < lines.Length; i++)
    {
        var m = Regex.Match(lines[i], @"^\s*@model\s+(.+?)\s*$");
        if (m.Success) { model = m.Groups[1].Value; lines[i] = ""; }
    }
    source = string.Join("\n", lines);
    var className = Regex.Replace(rel.Replace(".cshtml", ""), "[^A-Za-z0-9]", "_");

    var host = new RazorEngineHost(new CSharpRazorCodeLanguage())
    {
        DefaultBaseClass = isAppCode ? "System.Web.WebPages.HelperPage" : $"System.Web.Mvc.WebViewPage<{model}>",
        DefaultClassName = isAppCode ? Path.GetFileNameWithoutExtension(file) : className,
        DefaultNamespace = isAppCode ? "ASP" : "ASP.Views",
        StaticHelpers = isAppCode,
        GeneratedClassContext = new GeneratedClassContext("Execute", "Write", "WriteLiteral", "WriteTo", "WriteLiteralTo",
            "System.Web.WebPages.HelperResult", "DefineSection", "BeginContext", "EndContext") { ResolveUrlMethodName = "Href" },
    };
    foreach (var ns in namespaces) host.NamespaceImports.Add(ns);

    var engine = new RazorTemplateEngine(host);
    GeneratorResults result;
    using (var reader = new StringReader(source)) result = engine.GenerateCode(reader, host.DefaultClassName, host.DefaultNamespace, file);
    n++;
    if (!result.Success)
    {
        failed++;
        foreach (var e in result.ParserErrors) Console.WriteLine($"{rel}({e.Location.LineIndex + 1},{e.Location.CharacterIndex + 1}): error RZ: {e.Message}");
        continue;
    }
    if (isAppCode)
    {
        // like ASP.NET's WebCodeRazorHost: App_Code helper files only contain static @helper methods
        foreach (System.CodeDom.CodeNamespace ns in result.GeneratedCode.Namespaces)
            foreach (System.CodeDom.CodeTypeDeclaration type in ns.Types)
                foreach (var member in type.Members.Cast<System.CodeDom.CodeTypeMember>().Where(m => m.Name == "Execute").ToList())
                    type.Members.Remove(member);
    }
    using var w = new StringWriter();
    provider.GenerateCodeFromCompileUnit(result.GeneratedCode, w, new CodeGeneratorOptions());
    File.WriteAllText(Path.Combine(outDir, className + ".g.cs"), w.ToString());
}
Console.WriteLine($"razor-gen: {n} views, {failed} with parser errors");
return failed == 0 ? 0 : 1;
