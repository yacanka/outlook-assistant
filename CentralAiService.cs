using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Askai
{
    public sealed class CentralAiService
    {
        private static readonly HttpClient SharedClient = new HttpClient(
            new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        private readonly HttpClient client;

        public CentralAiService() : this(SharedClient) { }
        public CentralAiService(HttpClient client) { this.client = client ?? throw new ArgumentNullException(nameof(client)); }

        public async Task<string> SendAsync(string prompt, string model, string token,
            Action<string> onChunk, CancellationToken cancellationToken)
        {
            Uri uri;
            if (!Uri.TryCreate(AppConfig.CentralApiUrl, UriKind.Absolute, out uri)
                || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("Central servis için config içinde geçerli bir HTTPS URL tanımlayın.");
            if (string.IsNullOrWhiteSpace(model) || !AppConfig.CentralModels.Contains(model))
                throw new InvalidOperationException("AI Ayarları üzerinden yapılandırılmış bir model seçin.");
            if (string.IsNullOrWhiteSpace(token) || token.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new InvalidOperationException("AI Ayarları üzerinden geçerli bir token girin.");
            if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("İstek boş olamaz.");
            var payload = JsonSerializer.Serialize(new
            {
                model,
                stream = true,
                messages = new[] {
                    new { role = "system", content = AppConfig.SystemPrompt },
                    new { role = "user", content = prompt }
                }
            });
            if (Encoding.UTF8.GetByteCount(payload) > 1024 * 1024)
                throw new InvalidOperationException("AI isteği boyut sınırını aşıyor.");
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var request = new HttpRequestMessage(HttpMethod.Post, uri))
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(AppConfig.TimeoutMinutes));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                try
                {
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException("Central AI isteği başarısız (HTTP " + (int)response.StatusCode + ").");
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (deadline.Token.Register(() => stream.Dispose()))
                            return await ReadEventsAsync(stream, onChunk, deadline.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception) when (deadline.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("Central AI isteği zaman aşımına uğradı.");
                }
                catch (HttpRequestException)
                {
                    throw new HttpRequestException("Central AI servisine erişilemedi veya istek reddedildi.");
                }
            }
        }

        // Read bounded lines ourselves: ReadLineAsync can allocate an unbounded provider line.
        private static async Task<string> ReadEventsAsync(Stream stream, Action<string> onChunk, CancellationToken token)
        {
            var result = new StringBuilder();
            var line = new StringBuilder();
            var data = new StringBuilder();
            var buffer = new byte[4096];
            var chars = new char[4096];
            var decoder = new UTF8Encoding(false, true).GetDecoder();
            int total = 0;
            while (true)
            {
                int count = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (count == 0) throw InvalidStream();
                total += count;
                if (total > 1024 * 1024) throw InvalidStream();
                int length = decoder.GetChars(buffer, 0, count, chars, 0);
                for (int i = 0; i < length; i++)
                {
                    if (chars[i] != '\n') { line.Append(chars[i]); continue; }
                    string value = line.ToString().TrimEnd('\r');
                    line.Clear();
                    if (value.Length == 0 && data.Length > 0)
                    {
                        string json = data.ToString().TrimEnd('\n');
                        data.Clear();
                        if (json == "[DONE]") return result.ToString();
                        string chunk = ParseChunk(json);
                        if (!string.IsNullOrEmpty(chunk)) { result.Append(chunk); onChunk?.Invoke(chunk); }
                    }
                    else if (value.StartsWith("data:", StringComparison.Ordinal))
                        data.Append(value.Substring(5).TrimStart(' ')).Append('\n');
                    else if (value.StartsWith("event:", StringComparison.Ordinal)
                        && value.Substring(6).Trim() == "error") throw InvalidStream();
                }
            }
        }

        private static string ParseChunk(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    JsonElement choices, delta, content;
                    if (root.TryGetProperty("error", out content)) throw InvalidStream();
                    if (!root.TryGetProperty("choices", out choices) || choices.ValueKind != JsonValueKind.Array)
                        throw InvalidStream();
                    if (choices.GetArrayLength() == 0) return null; // Usage-only event.
                    JsonElement finishReason;
                    if (choices[0].TryGetProperty("finish_reason", out finishReason)
                        && finishReason.ValueKind != JsonValueKind.Null && finishReason.GetString() != "stop")
                        throw InvalidStream();
                    if (!choices[0].TryGetProperty("delta", out delta)) throw InvalidStream();
                    if (!delta.TryGetProperty("content", out content) || content.ValueKind == JsonValueKind.Null) return null;
                    if (content.ValueKind != JsonValueKind.String) throw InvalidStream();
                    return content.GetString();
                }
            }
            catch (JsonException) { throw InvalidStream(); }
            catch (InvalidOperationException) { throw InvalidStream(); }
        }
        private static IOException InvalidStream() { return new IOException("Central AI yanıt akışı geçersiz veya tamamlanmadı."); }
    }
}
