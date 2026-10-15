using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Askai;

internal static class ReliabilityTests
{
    public static async Task Run()
    {
        var failures = new List<string>();
        await CheckCase("bounded table rendering", () => { CheckTableLimits(); return Task.CompletedTask; }, failures);
        await CheckCase("central UTF-8 boundaries", () => CheckUnicode(false), failures);
        await CheckCase("legacy UTF-8 boundaries", () => CheckUnicode(true), failures);
        await CheckCase("linear multiline JSON", CheckMultilineJson, failures);
        await CheckCase("incremental preview buffer", () => { CheckPreviewBuffer(); return Task.CompletedTask; }, failures);
        await CheckCase("generated content bounds", () => { CheckGeneratedLimit(); return Task.CompletedTask; }, failures);
        await CheckCase("transport content bounds", CheckTransportLimits, failures);
        await CheckCase("background certificate cancellation", CheckCertificateWorker, failures);
        if (failures.Count > 0) throw new Exception(string.Join("; ", failures));
        Console.WriteLine("Reliability checks passed (table bounds, Unicode packet boundaries, incremental JSON).");
    }

    private static async Task CheckTransportLimits()
    {
        string originalUrl = AppConfig.CentralApiUrl;
        string[] originalModels = AppConfig.CentralModels;
        AppConfig.CentralApiUrl = "https://example.invalid/chat";
        AppConfig.CentralModels = new[] { "fixture-model" };
        try
        {
            foreach (bool legacy in new[] { false, true })
            foreach (int length in new[] { 64000, 64001 })
            {
                string expected = new string('x', length);
                string payload = legacy ? "{\"response\":\"" + expected + "\"}\n"
                    : "data: {\"choices\":[{\"delta\":{\"content\":\"" + expected + "\"}}]}\n\ndata: [DONE]\n\n";
                using (var client = Client(payload, legacy))
                {
                    int received = 0;
                    try
                    {
                        string result = legacy
                            ? await new LegacyAiService(client).SendStreamingRequestAsync("fixture", chunk => received += chunk.Length, default)
                            : await new CentralAiService(client).SendAsync("fixture", "fixture-model", "fixture-token", chunk => received += chunk.Length, default);
                        Check(length == 64000 && result == expected, "The maximum supported response is retained in full.");
                    }
                    catch (AiServiceException ex)
                    {
                        Check(length == 64001 && ex.Message.Contains("64.000"), "Response overflow has an actionable size error.");
                    }
                    Check(received <= 64000, "Transport never enqueues content past the preview budget.");
                }
            }
        }
        finally { AppConfig.CentralApiUrl = originalUrl; AppConfig.CentralModels = originalModels; }
    }

    private static async Task CheckCertificateWorker()
    {
        using (var started = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        using (var cancellation = new CancellationTokenSource())
        {
            int callerThread = Thread.CurrentThread.ManagedThreadId;
            var work = CertificateValidation.RunAsync(() =>
            {
                Check(Thread.CurrentThread.ManagedThreadId != callerThread, "Certificate work must not execute inline on its caller.");
                started.Set();
                release.Wait();
                return 1;
            }, cancellation.Token);
            try
            {
                Check(started.Wait(TimeSpan.FromSeconds(5)), "Background validation starts.");
                cancellation.Cancel();
                Check(await Task.WhenAny(work, Task.Delay(2000)) == work, "Cancellation must not wait for a native chain build.");
                bool cancelled = false;
                try { await work; } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "Cancelled certificate validation cannot return a usable result.");
                var queued = CertificateValidation.RunAsync(() => 2, CancellationToken.None);
                Check(!queued.IsCompleted, "Native certificate jobs remain serialized after a caller cancels.");
                release.Set();
                Check(await queued == 2, "A completed native job releases the validation slot.");
            }
            finally { release.Set(); }
        }
    }

    private static void CheckPreviewBuffer()
    {
        var buffer = new MailPreviewBuffer();
        long before = GC.GetTotalAllocatedBytes(true);
        for (int i = 0; i < 4000; i++)
            Check(buffer.Append("abcdefghijklmnop") == "abcdefghijklmnop", "A preview append emits only the newly received text.");
        Check(buffer.Source.Length == 64000, "Incremental preview keeps the full authoritative source.");
        Check(GC.GetTotalAllocatedBytes(true) - before < 2 * 1024 * 1024, "Preview buffering must not copy the whole prefix per chunk.");
        bool rejected = false;
        try { buffer.Append("x"); } catch (MailActionException) { rejected = true; }
        Check(rejected && buffer.Source.Length == 64000, "Overflow is rejected before growing the preview buffer.");
    }

    private static void CheckGeneratedLimit()
    {
        var session = new MailGenerationSession();
        bool rejected = false;
        try { session.Complete(new string('x', 64001)); } catch (MailActionException) { rejected = true; }
        Check(rejected && session.State == MailGenerationState.Failed, "Oversized results cannot enter the approval state.");
    }

    private static void CheckTableLimits()
    {
        var source = new StringBuilder("|");
        for (int i = 0; i < 400; i++) source.Append("x|");
        source.Append("\n|");
        for (int i = 0; i < 400; i++) source.Append("---|");
        source.Append('\n');
        for (int i = 0; i < 400; i++) source.Append("|x|\n");
        var content = MailFormatting.Create(source.ToString(), true);
        Check(content.Rtf.Length < 1024 * 1024, "A sparse wide table cannot expand past the RTF budget.");
        Check(content.PlainText.Contains("|x|"), "Oversized tables preserve their source as plain text.");
        var rows = "| A | B |\n| --- | --- |\n" + string.Concat(System.Linq.Enumerable.Repeat("| x | y |\n", 201));
        content = MailFormatting.Create(rows, true);
        Check(!content.Rtf.Contains("\\trowd"), "Oversized row counts use plain text without generating a partial table.");
        Check(content.PlainText.Contains("| x | y |"), "Row fallback retains the original content.");
        var normal = MailFormatting.Create("| A | B |\n| --- | --- |\n" + string.Concat(System.Linq.Enumerable.Repeat("| x | y |\n", 199)), true);
        Check(normal.Rtf.Contains("\\trowd"), "A supported 200-row table remains formatted.");
        var table = new StringBuilder("|");
        for (int i = 0; i < 12; i++) table.Append("A|");
        table.Append("\n|");
        for (int i = 0; i < 12; i++) table.Append("---|");
        table.Append('\n');
        for (int row = 0; row < 100; row++) table.Append('|').Append(string.Concat(System.Linq.Enumerable.Repeat("x|", 12))).Append('\n');
        Check(MailFormatting.Create(table.ToString(), true).Rtf.Contains("\\trowd"), "Twelve-column tables remain usable within the cell budget.");
        string twoTables = table + "\nSeparate\n\n" + table;
        content = MailFormatting.Create(twoTables, true);
        Check(!content.Rtf.Contains("\\trowd") && content.PlainText == twoTables,
            "The cell budget covers the entire mail and fallback retains earlier tables too.");
        string manyLists = string.Concat(System.Linq.Enumerable.Repeat("1. x\n\ntext\n\n", 5000));
        content = MailFormatting.Create(manyLists, true);
        Check(content.Rtf.Length < 1024 * 1024 && content.PlainText == manyLists,
            "Non-table Markdown expansion is bounded and falls back without truncation.");
    }

    private static async Task CheckUnicode(bool legacy)
    {
        string originalUrl = AppConfig.CentralApiUrl;
        string[] originalModels = AppConfig.CentralModels;
        AppConfig.CentralApiUrl = "https://example.invalid/chat";
        AppConfig.CentralModels = new[] { "fixture-model" };
        try
        {
            foreach (int split in new[] { 1, 2, 3 })
            {
                string prefix = legacy ? "{\"response\":\"" : "data: {\"choices\":[{\"delta\":{\"content\":\"";
                string suffix = legacy ? "\"}\n" : "\"}}]}\n\ndata: [DONE]\n\n";
                string expected = new string('a', 4096 - split - prefix.Length) + "😀" + new string('b', 5000);
                using (var client = Client(prefix + expected + suffix, legacy))
                {
                    string result = legacy
                        ? await new LegacyAiService(client).SendStreamingRequestAsync("fixture", null, default)
                        : await new CentralAiService(client).SendAsync("fixture", "fixture-model", "fixture-token", null, default);
                    Check(result == expected, "A valid four-byte character survives every 4096-byte boundary split.");
                }
            }
        }
        finally { AppConfig.CentralApiUrl = originalUrl; AppConfig.CentralModels = originalModels; }
    }

    private static async Task CheckMultilineJson()
    {
        string payload = "{\n\"response\":\"fixture\",\n\"padding\":[\n"
            + string.Concat(System.Linq.Enumerable.Repeat("0,\n", 4000)) + "0\n]}\n";
        using (var client = Client(payload, true))
        {
            long before = GC.GetTotalAllocatedBytes(true);
            string result = await new LegacyAiService(client).SendStreamingRequestAsync("fixture", null, default);
            long allocated = GC.GetTotalAllocatedBytes(true) - before;
            Check(result == "fixture", "Pretty-printed JSON retains the response field.");
            Check(allocated < 8 * 1024 * 1024, "A 12 KB JSON record must not allocate quadratically: " + allocated);
        }
    }

    private static HttpClient Client(string payload, bool legacy)
    {
        return new HttpClient(new FixtureHandler(payload, legacy));
    }

    private static async Task CheckCase(string name, Func<Task> test, List<string> failures)
    {
        try { await test(); Console.WriteLine("PASS: " + name); }
        catch (Exception ex) { failures.Add(name + ": " + ex.GetType().Name + " " + ex.Message); Console.WriteLine("FAIL: " + name); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly byte[] payload;
        private readonly bool legacy;
        public FixtureHandler(string payload, bool legacy) { this.payload = Encoding.UTF8.GetBytes(payload); this.legacy = legacy; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(payload)) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(legacy ? "application/json" : "text/event-stream");
            return Task.FromResult(response);
        }
    }
}
