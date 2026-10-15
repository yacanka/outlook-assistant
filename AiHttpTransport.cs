using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

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
        public static HttpClient GetClient(bool useTls12, bool forLegacy = false, string serverCertificateBase64 = null,
            CertificateTrustMode certificateTrustMode = CertificateTrustMode.ServerCertificate)
        {
            if (!string.IsNullOrEmpty(serverCertificateBase64))
            {
                // Revalidate lifetime on every request, including reuse of an existing TLS connection.
                var trust = ServerCertificateTrust.FromBase64(serverCertificateBase64, certificateTrustMode);
                return GetCertificateClient(useTls12, forLegacy, trust);
            }
            if (forLegacy) return (useTls12 ? legacyTls12 : legacyDefault).Value;
            return (useTls12 ? centralTls12 : centralDefault).Value;
        }

        internal static Task<HttpClient> GetClientAsync(bool useTls12, bool forLegacy, string certificate,
            CertificateTrustMode mode, CancellationToken token)
        {
            if (string.IsNullOrEmpty(certificate))
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(GetClient(useTls12, forLegacy));
            }
            return CertificateValidation.RunAsync(() => GetClient(useTls12, forLegacy, certificate, mode), token);
        }

        private static HttpClient GetCertificateClient(bool useTls12, bool forLegacy, ServerCertificateTrust trust)
        {
            string key = (forLegacy ? "legacy:" : "central:") + (useTls12 ? "tls12:" : "default:") + trust.Mode + ":" + trust.Fingerprint;
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
                handler.CheckCertificateRevocationList = trust.CheckRevocation;
                handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                {
                    string failure;
                    bool valid = trust.Validate(certificate, chain, errors, out failure);
                    // A shared handler can serve concurrent requests. Keep diagnostics on this request.
                    if (request != null)
                    {
#pragma warning disable CS0618 // Properties is required by the .NET Framework 4.7.2 production target.
                        request.Properties.Remove(AiConnectionErrors.CertificateFailureKey);
                        if (!valid) request.Properties[AiConnectionErrors.CertificateFailureKey] = failure;
#pragma warning restore CS0618
                    }
                    return valid;
                };
            }
            return handler;
        }
    }
}
