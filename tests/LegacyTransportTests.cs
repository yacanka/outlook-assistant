using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Askai;

internal static class LegacyTransportTests
{
    public static async Task Run()
    {
        var handler = new Handler();
        var service = new LegacyAiService(new HttpClient(handler));
        handler.Payload = "data: {\"choices\":[{\"delta\":{\"content\":\"Complete\"}}]}\n\ndata: [DONE]\n\n";
        var result = await service.SendStreamingRequestAsync("Synthetic prompt", null, default);
        Check(result == "Complete", "SSE terminator completes legacy response");
        handler.Payload = "data: {\"choices\":[{\"delta\":{\"content\":\"Partial\"}}]}\n\n";
        await Throws<IOException>(() => service.SendStreamingRequestAsync("prompt", null, default));
        handler.Payload = "data: {broken}\n\ndata: [DONE]\n\n";
        await Throws<IOException>(() => service.SendStreamingRequestAsync("prompt", null, default));
        handler.Payload = "data: {\"error\":\"sensitive upstream error\"}\n\ndata: [DONE]\n\n";
        await Throws<IOException>(() => service.SendStreamingRequestAsync("prompt", null, default));
        handler.Payload = "data: {\"choices\":[{\"delta\":{\"content\":\"Partial\"},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n\n";
        await Throws<IOException>(() => service.SendStreamingRequestAsync("prompt", null, default));
        handler.MediaType = "application/x-ndjson";
        handler.Payload = "{\"message\":{\"content\":\"Truncated\"},\"done\":true,\"done_reason\":\"length\"}\n";
        await Throws<IOException>(() => service.SendStreamingRequestAsync("prompt", null, default));
        handler.Payload = "{\"message\":{\"content\":\"First\"},\"done\":false}\n{\"message\":{\"content\":\" last\"},\"done\":true}\n";
        Check(await service.SendStreamingRequestAsync("prompt", null, default) == "First last", "Ollama final content retained");
        handler.Payload = "{\"response\":\"Partial\",\"done\":false}\n";
        await Throws<IOException>(() => service.SendStreamingRequestAsync("prompt", null, default));
        handler.MediaType = "application/json";
        handler.Payload = "{\"response\":\"Truncated\",\"done\":true,\"done_reason\":\"length\"}";
        await Throws<IOException>(() => service.SendStreamingRequestAsync("prompt", null, default));
        handler.Payload = "{\n\"response\":\"Standalone response\"\n}";
        Check(await service.SendStreamingRequestAsync("prompt", null, default) == "Standalone response", "standalone JSON supported");
        handler.Payload = "{\"message\":{\"content\":\"Partial\"},\"done\":false}";
        await Throws<IOException>(() => service.SendStreamingRequestAsync("prompt", null, default));
        handler.Payload = new string('x', 1024 * 1024 + 1);
        await Throws<IOException>(() => service.SendStreamingRequestAsync("prompt", null, default));
        handler.Status = HttpStatusCode.BadRequest;
        handler.Payload = "private provider response";
        try { await service.SendStreamingRequestAsync("prompt", null, default); throw new Exception("Expected HTTP failure"); }
        catch (HttpRequestException ex) { Check(!ex.Message.Contains("private"), "raw errors are hidden"); }
        handler.Status = HttpStatusCode.OK;
        handler.Stall = true;
        using (var cancel = new CancellationTokenSource(100))
            await Throws<OperationCanceledException>(() => service.SendStreamingRequestAsync("prompt", null, cancel.Token));
        Console.WriteLine("Legacy transport checks passed (completion, malformed/partial data, limits, cancellation).");
    }

    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
    private static async Task Throws<T>(Func<Task<string>> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private sealed class WaitingStream : MemoryStream
    {
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Payload;
        public string MediaType = "text/event-stream";
        public HttpStatusCode Status = HttpStatusCode.OK;
        public bool Stall;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = Stall ? new StreamContent(new WaitingStream()) : new StringContent(Payload, Encoding.UTF8, MediaType)
            });
        }
    }
}
