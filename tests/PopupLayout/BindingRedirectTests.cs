using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using System.Text.Json;

internal static class BindingRedirectTests
{
    internal static void Run()
    {
        string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string configPath = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
        if (!File.Exists(configPath))
            throw new Exception("Generated executable config is missing; build and distribute the complete MSBuild output.");

        var requested = typeof(JsonDocument).Assembly.GetReferencedAssemblies().Single(x => x.Name == "System.Memory");
        var shipped = AssemblyName.GetAssemblyName(Path.Combine(directory, "System.Memory.dll"));
        XNamespace ns = "urn:schemas-microsoft-com:asm.v1";
        var entry = XDocument.Load(configPath).Descendants(ns + "dependentAssembly")
            .Single(x => (string)x.Element(ns + "assemblyIdentity")?.Attribute("name") == "System.Memory");
        var identity = entry.Element(ns + "assemblyIdentity");
        var redirect = entry.Element(ns + "bindingRedirect");
        string token = string.Concat(shipped.GetPublicKeyToken().Select(x => x.ToString("x2")));
        Check((string)identity.Attribute("publicKeyToken") == token, "Redirect must target the shipped assembly identity.");
        Check((string)redirect.Attribute("newVersion") == shipped.Version.ToString(), "Redirect target must match the shipped DLL version.");
        string[] range = ((string)redirect.Attribute("oldVersion")).Split('-');
        var lower = Version.Parse(range[0]);
        var upper = Version.Parse(range[range.Length - 1]);
        Check(lower <= requested.Version && requested.Version <= upper, "Redirect must cover the JSON dependency's original version.");
        Check(Assembly.Load(requested).GetName().Version == shipped.Version, "Runtime must resolve the original reference to the shipped version.");
        using (var json = JsonDocument.Parse("{\"text\":\"Örnek\",\"value\":42}"))
            Check(json.RootElement.GetProperty("value").GetInt32() == 42, "JSON reader works with resolved dependencies.");
        Check(Markdig.Markdown.ToHtml("**Örnek**").Contains("<strong>Örnek</strong>"), "Markdown works with the same dependency set.");
        Console.WriteLine("Binding redirect checks passed (generated config, original reference, shipped DLL, JSON and Markdown).");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
    }
}
