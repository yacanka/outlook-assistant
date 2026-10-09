using System;
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
                Check(error.Message.Contains("HTTP 401") && error.Message.Contains("token"), "HTTP authentication cause must reach user");
                Check(!error.Message.Contains(handler.Payload), "provider body is never a user diagnostic");

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
                Check(!error.Message.Contains("private"), "raw exception text stays private");

                handler.Error = new HttpRequestException("Synthetic transport detail", new AuthenticationException("Handshake failed"));
                error = await ReadFailure(send);
                Check(error.Message.Contains("TLS"), "ambiguous handshake failure is diagnosed as TLS");

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
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Error != null) throw Error;
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(Payload, Encoding.UTF8, MediaType)
            });
        }
    }
}
