using Askai;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Askai
{
    public class AiService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(AppConfig.TimeoutMinutes)
        };

        /// <summary>
        /// AI API'ye streaming POST isteği gönderir.
        /// Her parça (chunk) geldiğinde onChunk callback'i çağrılır.
        /// Tüm yanıt tamamlandığında string olarak döner.
        /// </summary>
        public async Task<string> SendStreamingRequestAsync(
            string userPrompt,
            Action<string> onChunk,
            CancellationToken cancellationToken)
        {
            // ── İstek gövdesini oluştur ──
            var requestBody = new
            {
                chat_purpose = "2",
                strategy_type = 8,
                stream = true,
                max_tokens = 2048,

                num_rerank_candidates = 100,
                score_threshold = 0.25,
                top_k = 3,
                question = userPrompt,
                context_messages = new object[]
                {
                    new { role = "user", content = userPrompt}
                }
            };

            string jsonBody = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            // ── HTTP isteğini hazırla ──
            var request = new HttpRequestMessage(HttpMethod.Post, AppConfig.ApiUrl)
            {
                Content = httpContent
            };

            // API anahtarı varsa ekle
            if (!string.IsNullOrWhiteSpace(AppConfig.ApiKey))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", AppConfig.ApiKey);
            }

            // ── Streaming yanıtı al (header gelince devam et, body'yi beklemeden) ──
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync()
                    .ConfigureAwait(false);
                throw new HttpRequestException(
                    $"API Hatası ({(int)response.StatusCode}): {errorBody}");
            }

            // ── Stream'i satır satır oku ──
            var fullResponse = new StringBuilder();

            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    line += "\n\n";
                    string contentChunk = ExtractContentFromLine(line);

                    if (contentChunk != null)
                    {
                        // ── Literal \n ve \r escape'lerini gerçek karaktere çevir ──
                        // Bazı API'ler JSON escape dışında literal \\n gönderebiliyor
                        contentChunk = contentChunk
                            .Replace("\\r\\n", "\r\n")
                            .Replace("\\n", "\n")
                            .Replace("\\r", "\r");

                        fullResponse.Append(contentChunk);
                        onChunk?.Invoke(contentChunk);
                    }
                }
            }

            return fullResponse.ToString();
        }

        /// <summary>
        /// Gelen satırdan içerik metnini çıkarır.
        /// OpenAI SSE formatı, Ollama formatı ve düz JSON destekler.
        /// </summary>
        private string ExtractContentFromLine(string line)
        {
            // SSE (Server-Sent Events) formatı: "data: {...}"
            string jsonPart = line;
            if (line.StartsWith("data:"))
            {
                jsonPart = line.Substring(5).Trim();
            }

            // Stream sonu işareti
            if (jsonPart == "[DONE]")
                return null;

            try
            {
                using (var doc = JsonDocument.Parse(jsonPart))
                {
                    var root = doc.RootElement;

                    // ── Format 1: OpenAI uyumlu ──
                    // {"choices":[{"delta":{"content":"merhaba"}}]}
                    if (root.TryGetProperty("choices", out var choices)
                        && choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];

                        // Streaming: delta.content
                        if (firstChoice.TryGetProperty("delta", out var delta)
                            && delta.TryGetProperty("content", out var deltaContent))
                        {
                            return deltaContent.GetString();
                        }

                        // Non-streaming: message.content
                        if (firstChoice.TryGetProperty("message", out var msg)
                            && msg.TryGetProperty("content", out var msgContent))
                        {
                            return msgContent.GetString();
                        }
                    }

                    // ── Format 2: Ollama ──
                    // {"message":{"content":"merhaba"},"done":false}
                    if (root.TryGetProperty("message", out var ollamaMsg)
                        && ollamaMsg.TryGetProperty("content", out var ollamaContent))
                    {
                        // Ollama "done":true geldiğinde boş content gelir
                        if (root.TryGetProperty("done", out var done) && done.GetBoolean())
                            return null;

                        return ollamaContent.GetString();
                    }

                    // ── Format 3: Basit format ��─
                    // {"response":"merhaba"} veya {"text":"merhaba"} veya {"content":"merhaba"}
                    foreach (string prop in new[] { "response", "text", "content", "output" })
                    {
                        if (root.TryGetProperty(prop, out var val)
                            && val.ValueKind == JsonValueKind.String)
                        {
                            return val.GetString();
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // JSON değilse düz metin olarak kabul et
                // (SSE prefix'i çıkarılmış hali)
                if (!line.StartsWith("event:") && !line.StartsWith("id:") && !line.StartsWith(":"))
                {
                    return jsonPart;
                }
            }

            return null;
        }
    }
}