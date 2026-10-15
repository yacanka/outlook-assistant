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
        private readonly HttpClient client;

        public LegacyAiService() : this(AiHttpTransport.GetClient(false, forLegacy: true)) { }
        public LegacyAiService(HttpClient client) { this.client = client ?? throw new ArgumentNullException(nameof(client)); }

        /// <summary>
        /// Retains the TAB 3.2 line-oriented response contract, including raw text and clean EOF.
        /// Explicit incomplete/error records, invalid JSON and transport failures are rejected.
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
                        AiConnectionErrors.CheckResponse(response, AiBrand.Tab32, requireEventStream: false);
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (deadline.Token.Register(() => stream.Dispose()))
                        {
                            return await ReadStreamAsync(stream, onChunk, deadline.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception) when (deadline.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("TAB 3.2 isteği zaman aşımına uğradı.");
                }
                catch (DecoderFallbackException) { throw InvalidStream(); }
                catch (HttpRequestException ex) when (!(ex is AiServiceException))
                {
                    throw AiConnectionErrors.FromTransport(AiBrand.Tab32, ex);
                }
            }
        }

        private static async Task<string> ReadStreamAsync(Stream stream, Action<string> onChunk, CancellationToken token)
        {
            var parser = new ResponseStream(onChunk);
            var buffer = new byte[4096];
            var encoding = new UTF8Encoding(false, true);
            var chars = new char[encoding.GetMaxCharCount(buffer.Length)];
            var decoder = encoding.GetDecoder();
            var line = new StringBuilder();
            int total = 0;
            while (true)
            {
                int count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (count == 0)
                {
                    decoder.GetChars(buffer, 0, 0, chars, 0, true); // Reject an incomplete UTF-8 sequence.
                    if (line.Length > 0 && parser.ProcessLine(line.ToString().TrimEnd('\r')))
                        return parser.Result;
                    return parser.Complete();
                }
                token.ThrowIfCancellationRequested();
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
            private readonly StringBuilder pendingJson = new StringBuilder();
            private readonly Action<string> onChunk;
            private bool requiresCompletion;
            private int jsonDepth;
            private bool jsonString;
            private bool jsonEscape;
            public string Result => result.ToString();
            public ResponseStream(Action<string> onChunk) { this.onChunk = onChunk; }

            public bool ProcessLine(string line)
            {
                if (line.StartsWith("event:", StringComparison.Ordinal))
                {
                    if (line.Substring(6).Trim() == "error") throw InvalidStream();
                    return false;
                }
                if (line.StartsWith(":", StringComparison.Ordinal) || line.StartsWith("id:", StringComparison.Ordinal)
                    || line.StartsWith("retry:", StringComparison.Ordinal)) return false;
                bool dataRecord = line.StartsWith("data:", StringComparison.Ordinal);
                if (dataRecord) line = line.Substring(5).TrimStart(' ');
                if (string.IsNullOrWhiteSpace(line)) return false;
                if (line.Trim() == "[DONE]")
                {
                    if (pendingJson.Length != 0) throw InvalidStream();
                    return true;
                }

                // This provider also sends consecutive data: lines without SSE blank boundaries.
                // Decode complete records immediately, while allowing pretty-printed JSON documents.
                string trimmed = line.TrimStart();
                if (pendingJson.Length > 0 || trimmed.StartsWith("{") || trimmed.StartsWith("["))
                    return AppendJson(line);
                if (trimmed.StartsWith("<")) throw InvalidStream(); // Do not turn an HTML error page into mail.
                // The original plain-text response format separates physical lines as paragraphs.
                AppendText(dataRecord ? line : line + "\n\n");
                return false;
            }

            private bool AppendJson(string line)
            {
                pendingJson.Append(line).Append('\n');
                // Frame records in one pass. Parsing the entire prefix on each physical line is quadratic.
                foreach (char value in line)
                {
                    if (jsonString)
                    {
                        if (jsonEscape) jsonEscape = false;
                        else if (value == '\\') jsonEscape = true;
                        else if (value == '"') jsonString = false;
                    }
                    else if (value == '"') jsonString = true;
                    else if (value == '{' || value == '[') { if (++jsonDepth > 64) throw InvalidStream(); }
                    else if (value == '}' || value == ']') { if (--jsonDepth < 0) throw InvalidStream(); }
                }
                if (jsonString) throw InvalidStream(); // Literal newlines are not valid inside JSON strings.
                if (jsonDepth != 0) return false;
                try
                {
                    using (var document = JsonDocument.Parse(pendingJson.ToString()))
                    {
                        pendingJson.Clear();
                        JsonElement done;
                        if (document.RootElement.ValueKind != JsonValueKind.Object) throw InvalidStream();
                        if (document.RootElement.TryGetProperty("done", out done)) requiresCompletion = !done.GetBoolean();
                        bool completed;
                        AppendText(ParsePayload(document.RootElement, out completed));
                        return completed;
                    }
                }
                catch (JsonException) { throw InvalidStream(); }
                catch (InvalidOperationException) { throw InvalidStream(); }
            }

            private void AppendText(string chunk)
            {
                if (string.IsNullOrEmpty(chunk)) return;
                chunk = chunk.Replace("\\r\\n", "\r\n").Replace("\\n", "\n").Replace("\\r", "\r");
                if (chunk.Length > MailContentLimits.GeneratedCharacters - result.Length)
                    throw new AiServiceException("AI yanıtı 64.000 karakter sınırını aşıyor. Daha kısa bir içerik isteyin; mail değiştirilmedi.");
                result.Append(chunk);
                onChunk?.Invoke(chunk);
            }

            public string Complete()
            {
                // Older TAB 3.2 servers have no terminator. A clean HTTP EOF is their contract;
                // explicit done:false, broken records and read failures must still fail closed.
                if (pendingJson.Length != 0 || requiresCompletion || result.Length == 0) throw InvalidStream();
                return Result;
            }
        }

        private static string ParsePayload(JsonElement root, out bool completed)
        {
            completed = false;
            try
            {
                JsonElement value;
                if (root.TryGetProperty("error", out value)) throw InvalidStream();
                if (root.TryGetProperty("done_reason", out value) && value.ValueKind != JsonValueKind.Null
                    && value.GetString() != "stop") throw InvalidStream();
                bool hasDone = root.TryGetProperty("done", out value);
                if (hasDone) completed = value.GetBoolean();
                JsonElement choices;
                if (root.TryGetProperty("choices", out choices))
                    return ParseChoice(choices, ref completed);
                if (root.TryGetProperty("message", out value)) return ReadContent(value);
                foreach (string property in new[] { "response", "text", "content", "output" })
                    if (root.TryGetProperty(property, out value)) return value.GetString();
                if (hasDone) return "";
                throw InvalidStream();
            }
            catch (JsonException) { throw InvalidStream(); }
            catch (InvalidOperationException) { throw InvalidStream(); }
        }

        private static string ParseChoice(JsonElement choices, ref bool completed)
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

        private static IOException InvalidStream() { return new AiResponseException(AiBrand.Tab32); }
    }
}
