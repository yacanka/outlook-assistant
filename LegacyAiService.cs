using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Askai
{
    public class LegacyAiService
    {
        private const int MaxBytes = 1024 * 1024;
        private static readonly HttpClient SharedClient = new HttpClient(
            new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        private readonly HttpClient client;

        public LegacyAiService() : this(SharedClient) { }
        public LegacyAiService(HttpClient client) { this.client = client ?? throw new ArgumentNullException(nameof(client)); }

        /// <summary>
        /// Retains the Legacy request contract. SSE requires [DONE], NDJSON requires done:true;
        /// a standalone application/json document must contain a complete response.
        /// Partial data, provider errors and malformed records never become a mail draft.
        /// </summary>
        public async Task<string> SendStreamingRequestAsync(string userPrompt, Action<string> onChunk,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(userPrompt)) throw new ArgumentException("İstek boş olamaz.");
            string payload = JsonSerializer.Serialize(new
            {
                chat_purpose = "2", strategy_type = 8, stream = true, max_tokens = 2048,
                num_rerank_candidates = 100, score_threshold = 0.25, top_k = 3,
                question = userPrompt,
                context_messages = new object[] { new { role = "user", content = userPrompt } }
            });
            if (Encoding.UTF8.GetByteCount(payload) > MaxBytes)
                throw new InvalidOperationException("AI isteği boyut sınırını aşıyor.");

            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var request = new HttpRequestMessage(HttpMethod.Post, AppConfig.ApiUrl))
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(AppConfig.TimeoutMinutes));
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                if (!string.IsNullOrWhiteSpace(AppConfig.ApiKey))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AppConfig.ApiKey);
                try
                {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException("Legacy AI isteği başarısız.");
                        string mediaType = response.Content.Headers.ContentType?.MediaType;
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (deadline.Token.Register(() => stream.Dispose()))
                        {
                            if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                                return await ReadDocumentAsync(stream, onChunk, deadline.Token).ConfigureAwait(false);
                            return await ReadStreamAsync(stream, onChunk, deadline.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception) when (deadline.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("Legacy AI isteği zaman aşımına uğradı.");
                }
                catch (HttpRequestException)
                {
                    throw new HttpRequestException("Legacy AI servisine erişilemedi veya istek reddedildi.");
                }
            }
        }

        private static async Task<string> ReadDocumentAsync(Stream stream, Action<string> onChunk, CancellationToken token)
        {
            using (var body = new MemoryStream())
            {
                var buffer = new byte[4096];
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
                {
                    if (body.Length + count > MaxBytes) throw InvalidStream();
                    body.Write(buffer, 0, count);
                }
                bool completed;
                string result = ParsePayload(new UTF8Encoding(false, true).GetString(body.ToArray()), true, out completed);
                if (!completed) throw InvalidStream();
                onChunk?.Invoke(result);
                return result;
            }
        }

        private static async Task<string> ReadStreamAsync(Stream stream, Action<string> onChunk, CancellationToken token)
        {
            var parser = new ResponseStream(onChunk);
            var buffer = new byte[4096];
            var chars = new char[4096];
            var decoder = new UTF8Encoding(false, true).GetDecoder();
            var line = new StringBuilder();
            int total = 0;
            while (true)
            {
                int count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (count == 0)
                {
                    // A final NDJSON record may lack a newline. SSE still needs its event boundary.
                    if (line.Length > 0 && parser.ProcessLine(line.ToString().TrimEnd('\r')))
                        return parser.Result;
                    throw InvalidStream();
                }
                total += count;
                if (total > MaxBytes) throw InvalidStream();
                int length = decoder.GetChars(buffer, 0, count, chars, 0);
                for (int i = 0; i < length; i++)
                {
                    if (chars[i] != '\n') { line.Append(chars[i]); continue; }
                    string value = line.ToString().TrimEnd('\r');
                    line.Clear();
                    if (parser.ProcessLine(value)) return parser.Result;
                }
            }
        }

        private sealed class ResponseStream
        {
            private readonly StringBuilder result = new StringBuilder();
            private readonly StringBuilder eventData = new StringBuilder();
            private readonly Action<string> onChunk;
            public string Result => result.ToString();
            public ResponseStream(Action<string> onChunk) { this.onChunk = onChunk; }

            public bool ProcessLine(string line)
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    eventData.Append(line.Substring(5).TrimStart(' ')).Append('\n');
                    return false;
                }
                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    if (line.Substring(6).Trim() == "error") throw InvalidStream();
                    return false;
                }
                if (line.StartsWith(":", StringComparison.Ordinal) || line.StartsWith("id:", StringComparison.Ordinal)
                    || line.StartsWith("retry:", StringComparison.Ordinal)) return false;
                if (line.Length == 0)
                {
                    if (eventData.Length == 0) return false;
                    string payload = eventData.ToString().TrimEnd('\n');
                    eventData.Clear();
                    return AppendPayload(payload);
                }
                if (eventData.Length > 0) throw InvalidStream();
                return AppendPayload(line);
            }

            private bool AppendPayload(string payload)
            {
                if (payload == "[DONE]") return true;
                bool completed;
                string chunk = ParsePayload(payload, false, out completed);
                if (!string.IsNullOrEmpty(chunk))
                {
                    // Preserve the Legacy provider's literal newline convention.
                    chunk = chunk.Replace("\\r\\n", "\r\n").Replace("\\n", "\n").Replace("\\r", "\r");
                    result.Append(chunk);
                    onChunk?.Invoke(chunk);
                }
                return completed;
            }
        }

        private static string ParsePayload(string json, bool standalone, out bool completed)
        {
            completed = false;
            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    JsonElement value;
                    if (root.TryGetProperty("error", out value)) throw InvalidStream();
                    if (root.TryGetProperty("done_reason", out value) && value.ValueKind != JsonValueKind.Null
                        && value.GetString() != "stop") throw InvalidStream();
                    bool hasDone = root.TryGetProperty("done", out value);
                    if (hasDone) completed = value.GetBoolean();
                    else completed = standalone;
                    JsonElement choices;
                    if (root.TryGetProperty("choices", out choices))
                        return ParseChoice(choices, standalone, ref completed);
                    if (root.TryGetProperty("message", out value)) return ReadContent(value);
                    foreach (string property in new[] { "response", "text", "content", "output" })
                        if (root.TryGetProperty(property, out value)) return value.GetString();
                    if (hasDone) return "";
                    throw InvalidStream();
                }
            }
            catch (JsonException) { throw InvalidStream(); }
            catch (InvalidOperationException) { throw InvalidStream(); }
        }

        private static string ParseChoice(JsonElement choices, bool standalone, ref bool completed)
        {
            if (choices.GetArrayLength() == 0)
            {
                completed = false;
                return "";
            }
            var choice = choices[0];
            JsonElement reason, value;
            if (choice.TryGetProperty("finish_reason", out reason) && reason.ValueKind != JsonValueKind.Null
                && reason.GetString() != "stop") throw InvalidStream();
            if (choice.TryGetProperty("delta", out value))
            {
                completed = false;
                if (standalone) throw InvalidStream();
                return ReadContent(value);
            }
            if (choice.TryGetProperty("message", out value)) return ReadContent(value);
            throw InvalidStream();
        }

        private static string ReadContent(JsonElement value)
        {
            JsonElement content;
            if (!value.TryGetProperty("content", out content) || content.ValueKind == JsonValueKind.Null) return "";
            return content.GetString();
        }

        private static IOException InvalidStream() { return new IOException("Legacy AI yanıtı geçersiz veya tamamlanmadı."); }
    }
}
