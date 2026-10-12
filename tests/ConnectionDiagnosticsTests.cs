using System;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Askai;

internal static class ConnectionDiagnosticsTests
{
    public static async Task Run()
    {
        foreach (bool central in new[] { true, false })
        {
            var handler = new Handler();
            using (var client = new HttpClient(handler))
            {
                var centralService = new CentralAiService(client);
                var legacyService = new LegacyAiService(client);
                Func<Task<string>> send = central
                    ? () => centralService.SendAsync("Synthetic request", "test-model", "test-token", null, CancellationToken.None)
                    : () => legacyService.SendStreamingRequestAsync("Synthetic request", null, CancellationToken.None);

                handler.Status = HttpStatusCode.Unauthorized;
                var error = await ReadFailure(send);
                Check(error.Message.StartsWith(central ? "PLUS AI (HTTP 401)" : "TAB 3.2 (HTTP 401)"),
                    "Diagnostics identify the service by its visible name without repeating AI");
                Check(error.Message.Contains("HTTP 401") && error.Message.Contains("token"), "HTTP authentication cause must reach user");
                Check(!error.Message.Contains(handler.Payload), "provider body is never a user diagnostic");

                handler.Status = HttpStatusCode.MethodNotAllowed;
                handler.AllowedMethods = new[] { "GET", "HEAD", "OPTIONS", "private-provider-data" };
                error = await ReadFailure(send);
                Check(error.Message.Contains("HTTP 405") && error.Message.Contains("POST")
                    && error.Message.Contains("API"), "405 explains the rejected POST and endpoint check");
                Check(error.Message.Contains("GET, HEAD, OPTIONS") && !error.Message.Contains("private-provider-data")
                    && !error.Message.Contains(handler.Payload), "405 exposes only known allowed methods");
                Check(handler.Method == HttpMethod.Post, "AI request remains POST; TLS/diagnostics cannot change the method");
                if (central)
                    Check(handler.Address == new Uri(AppConfig.CentralApiUrl), "Central sends to the exact configured endpoint");
                handler.AllowedMethods = null;
                error = await ReadFailure(send);
                Check(error.Message.Contains("HTTP 405") && error.Message.Contains("POST"), "405 without Allow remains actionable");

                handler.Status = HttpStatusCode.Redirect;
                error = await ReadFailure(send);
                Check(error.Message.Contains("HTTP 302") && error.Message.Contains("yönlendirme"), "redirect must not be mistaken for TLS");

                handler.Status = HttpStatusCode.OK;
                error = await ReadFailure(send);
                Check(error.Message.Contains("HTTP 200") && error.Message.Contains("text/html"), "HTML response is identified before parsing");
                Check(!error.Message.Contains(handler.Payload), "HTML/login content stays private");
                if (central)
                {
                    handler.MediaType = "application/json";
                    error = await ReadFailure(send);
                    Check(error.Message.Contains("application/json") && error.Message.Contains("text/event-stream"), "nonstreaming response is identified");
                    handler.MediaType = "text/html";
                }

                var original = new HttpRequestException("Synthetic private transport detail",
                    new WebException("Synthetic private certificate detail", WebExceptionStatus.TrustFailure));
                handler.Error = original;
                error = await ReadFailure(send);
                Check(error.Message.Contains("sertifika") && ReferenceEquals(error.InnerException, original), "certificate cause preserved without leaking details");
                Check(error.Message.StartsWith(central ? "PLUS AI:" : "TAB 3.2:"),
                    "Transport failures use the same visible service names as HTTP diagnostics");
                Check(!error.Message.Contains("private"), "raw exception text stays private");

                handler.Error = new HttpRequestException("Synthetic transport detail", new AuthenticationException("Handshake failed"));
                error = await ReadFailure(send);
                Check(error.Message.Contains("TLS"), "ambiguous handshake failure is diagnosed as TLS");
                handler.Error = new HttpRequestException("Synthetic transport detail",
                    new WebException("Synthetic private detail", new Win32Exception(unchecked((int)0x80090326)),
                        WebExceptionStatus.SecureChannelFailure, null));
                error = await ReadFailure(send);
                Check(error.Message.Contains("0x80090326") && !error.Message.Contains("private"),
                    "native TLS diagnostic code reaches UI without private exception text");

                if (central)
                {
                    handler.CertificateFailure = "Sertifika iptal durumu doğrulanamadı (CRL/OCSP).";
                    error = await ReadFailure(send);
                    Check(error.Message.Contains("CRL/OCSP"), "Central passes the request's certificate diagnostic to UI");
                    handler.CertificateFailure = null;
                }

                handler.Error = new HttpRequestException("Synthetic transport detail", new SocketException((int)SocketError.HostNotFound));
                error = await ReadFailure(send);
                Check(error.Message.Contains("DNS"), "DNS failure is distinct from TLS");

                handler.Error = new HttpRequestException("Synthetic transport detail", new SocketException((int)SocketError.ConnectionRefused));
                error = await ReadFailure(send);
                Check(error.Message.Contains("bağlantı"), "connection failure remains actionable");
            }
        }
        Console.WriteLine("Connection diagnostics passed (HTTP, HTML/JSON, certificate/TLS, DNS, private details).");
    }

    private static async Task<HttpRequestException> ReadFailure(Func<Task<string>> send)
    {
        try { await send(); }
        catch (HttpRequestException error) { return error; }
        throw new Exception("Expected classified connection failure");
    }

    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }

    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status;
        public string MediaType = "text/html";
        public string Payload = "<html>Synthetic private login page</html>";
        public Exception Error;
        public string CertificateFailure;
        public string[] AllowedMethods;
        public HttpMethod Method;
        public Uri Address;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Method = request.Method;
            Address = request.RequestUri;
#pragma warning disable CS0618
            if (CertificateFailure != null) request.Properties[AiConnectionErrors.CertificateFailureKey] = CertificateFailure;
#pragma warning restore CS0618
            if (Error != null) throw Error;
            var response = new HttpResponseMessage(Status)
            {
                Content = new StringContent(Payload, Encoding.UTF8, MediaType)
            };
            if (AllowedMethods != null) response.Content.Headers.TryAddWithoutValidation("Allow", AllowedMethods);
            return Task.FromResult(response);
        }
    }
}
