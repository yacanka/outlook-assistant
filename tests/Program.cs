using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Askai;
class Program
{
    static async Task Main()
    {
        MailGenerationTests.Run();
        TlsPolicyTests.Run();
        ServerCertificateTests.Run();
        CertificateChainTests.Run();
        OutlookAdapterTests.Run();
        await LegacyTransportTests.Run();
        AppConfig.CentralApiUrl = "https://example.invalid/chat";
        AppConfig.CentralModels = new[] { "test-model" };
        await ConnectionDiagnosticsTests.Run();
        var handler = new FakeHandler();
        var service = new CentralAiService(new HttpClient(handler));
        var chunks = new List<string>();
        var answer = await service.SendAsync("prompt", "test-model", "test-token", chunks.Add, CancellationToken.None);
        Check(answer == "Hello\nworld" && chunks.Count == 2, "stream chunks");
        using (var body = JsonDocument.Parse(handler.Body))
        {
            Check(body.RootElement.GetProperty("stream").GetBoolean(), "stream enabled");
            Check(body.RootElement.GetProperty("model").GetString() == "test-model", "model");
            Check(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() == "prompt", "prompt");
        }
        Check(handler.Token == "test-token", "authorization");
        await Fails(() => service.SendAsync("prompt", "unknown", "test-token", null, default), "invalid model");
        await Fails(() => service.SendAsync("prompt", "test-model", "", null, default), "missing token");
        handler.Payload = "data: {broken}\n\n";
        await Fails(() => service.SendAsync("prompt", "test-model", "test-token", null, default), "malformed stream");
        handler.Payload = "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n";
        await Fails(() => service.SendAsync("prompt", "test-model", "test-token", null, default), "token limit is not a completed mail");
        handler.Payload = "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n";
        await Fails(() => service.SendAsync("prompt", "test-model", "test-token", null, default), "truncated stream");
        handler.Status = HttpStatusCode.Unauthorized;
        await Fails(() => service.SendAsync("prompt", "test-model", "test-token", null, default), "HTTP failure");
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            await Fails(() => service.SendAsync("prompt", "test-model", "test-token", null, cts.Token), "cancellation");
        }
        handler.Status = HttpStatusCode.OK;
        handler.Stall = true;
        using (var cts = new CancellationTokenSource(100))
        {
            try
            {
                await service.SendAsync("prompt", "test-model", "test-token", null, cts.Token);
                throw new Exception("Expected cancellation during body read");
            }
            catch (OperationCanceledException) { }
        }
        Console.WriteLine("All transport checks passed (including cancellation during body read).");
    }
    static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
    static async Task Fails(Func<Task<string>> action, string name)
    {
        try { await action(); } catch { return; }
        throw new Exception("Expected failure: " + name);
    }
    class WaitingStream : MemoryStream
    {
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }
    }
    class FakeHandler : HttpMessageHandler
    {
        public string Body, Token;
        public bool Stall;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Payload = ": heartbeat\n\ndata: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"Hello\\n\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"world\"}}]}\n\ndata: [DONE]\n\n";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Body = await request.Content.ReadAsStringAsync(); Token = request.Headers.Authorization.Parameter;
            return new HttpResponseMessage(Status) { Content = Stall ? new StreamContent(new WaitingStream()) : new StringContent(Payload, Encoding.UTF8, "text/event-stream") };
        }
    }
}
