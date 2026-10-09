using System;
using System.Security.Authentication;
using Askai;

internal static class TlsPolicyTests
{
    public static void Run()
    {
        using (var standard = AiHttpTransport.CreateHandler(false))
        using (var compatibility = AiHttpTransport.CreateHandler(true))
        {
            Check(standard.SslProtocols == SslProtocols.None, "existing TLS choice remains the default");
            Check(compatibility.SslProtocols == SslProtocols.Tls12, "compatibility mode isolates TLS 1.2 to the AI client");
            Check(standard.ServerCertificateCustomValidationCallback == null
                && compatibility.ServerCertificateCustomValidationCallback == null, "certificate verification is never bypassed");
            Check(standard.UseProxy && compatibility.UseProxy, "corporate proxy policy remains active");
            Check(!standard.AllowAutoRedirect && !compatibility.AllowAutoRedirect, "tokens cannot follow automatic redirects");
        }
        var standardClient = AiHttpTransport.GetClient(false);
        var compatibilityClient = AiHttpTransport.GetClient(true);
        Check(!ReferenceEquals(standardClient, compatibilityClient), "mode switch uses a separate connection pool");
        Check(!ReferenceEquals(standardClient, AiHttpTransport.GetClient(false, forLegacy: true))
            && !ReferenceEquals(compatibilityClient, AiHttpTransport.GetClient(true, forLegacy: true)),
            "provider cookie and connection state stays isolated");
        Check(ReferenceEquals(standardClient, AiHttpTransport.GetClient(false))
            && ReferenceEquals(compatibilityClient, AiHttpTransport.GetClient(true)), "clients and pools are reused across requests");
        Console.WriteLine("TLS policy checks passed (opt-in TLS 1.2, certificate/proxy protection, isolated pools).");
    }

    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
}
