using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;

namespace Askai
{
    internal static class AiHttpTransport
    {
        private static readonly Lazy<HttpClient> centralDefault = CreateClient(false);
        private static readonly Lazy<HttpClient> centralTls12 = CreateClient(true);
        private static readonly Lazy<HttpClient> legacyDefault = CreateClient(false);
        private static readonly Lazy<HttpClient> legacyTls12 = CreateClient(true);
        private const int MaxCertificateClients = 16;
        private static readonly object certificatePoolLock = new object();
        private static readonly Dictionary<string, Lazy<HttpClient>> certificateClients =
            new Dictionary<string, Lazy<HttpClient>>(StringComparer.Ordinal);

        // Pools are separate by provider and TLS mode: changing settings must not reuse a
        // connection negotiated with the previous policy or share provider cookie state.
        public static HttpClient GetClient(bool useTls12, bool forLegacy = false, string serverCertificateBase64 = null)
        {
            if (!string.IsNullOrEmpty(serverCertificateBase64))
            {
                if (forLegacy)
                    throw new AiServiceException("Manuel sunucu sertifikası yalnızca Central HTTPS bağlantısında kullanılabilir.");
                // Revalidate lifetime on every request, including reuse of an existing TLS connection.
                var trust = ServerCertificateTrust.FromBase64(serverCertificateBase64);
                return GetCertificateClient(useTls12, trust);
            }
            if (forLegacy) return (useTls12 ? legacyTls12 : legacyDefault).Value;
            return (useTls12 ? centralTls12 : centralDefault).Value;
        }

        private static HttpClient GetCertificateClient(bool useTls12, ServerCertificateTrust trust)
        {
            string key = (useTls12 ? "tls12:" : "default:") + trust.Fingerprint;
            Lazy<HttpClient> client;
            lock (certificatePoolLock)
            {
                if (!certificateClients.TryGetValue(key, out client))
                {
                    // Bound resources without disposing pools that may still have in-flight requests.
                    if (certificateClients.Count >= MaxCertificateClients)
                        throw new AiServiceException("Sertifika ayarları çok kez değiştirildi. Yeni ayarları uygulamak için Outlook'u yeniden başlatın.");
                    client = CreateClient(useTls12, trust);
                    certificateClients.Add(key, client);
                }
            }
            return client.Value;
        }

        private static Lazy<HttpClient> CreateClient(bool useTls12, ServerCertificateTrust trust = null)
        {
            return new Lazy<HttpClient>(() => new HttpClient(CreateHandler(useTls12, trust))
            {
                Timeout = Timeout.InfiniteTimeSpan
            });
        }

        public static HttpClientHandler CreateHandler(bool useTls12, ServerCertificateTrust trust = null)
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            // Per-handler override: never mutate ServicePointManager in the Outlook host.
            // With no manual certificate, normal Windows trust checks are unchanged.
            if (useTls12) handler.SslProtocols = SslProtocols.Tls12;
            if (trust != null)
            {
                handler.CheckCertificateRevocationList = true;
                handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                    trust.Validate(certificate, chain, errors);
            }
            return handler;
        }
    }
}
