using System;
using System.Threading;
using System.Threading.Tasks;

namespace Askai
{
    public class AiService
    {
        public Task<string> SendStreamingRequestAsync(string userPrompt, Action<string> onChunk,
            CancellationToken cancellationToken)
        {
            return SendStreamingRequestAsync(userPrompt, onChunk, cancellationToken, AiSettings.Load());
        }

        internal Task<string> SendStreamingRequestAsync(string userPrompt, Action<string> onChunk,
            CancellationToken cancellationToken, AiSettings settings)
        {
            if (settings.Provider == AiProvider.Legacy)
                return new LegacyAiService(AiHttpTransport.GetClient(settings.UseTls12, forLegacy: true,
                    serverCertificateBase64: settings.ActiveLegacyCertificate,
                    certificateTrustMode: settings.LegacyCertificateTrustMode))
                    .SendStreamingRequestAsync(userPrompt, onChunk, cancellationToken);
            return new CentralAiService(AiHttpTransport.GetClient(settings.UseTls12,
                    serverCertificateBase64: settings.ActiveCentralCertificate,
                    certificateTrustMode: settings.CentralCertificateTrustMode))
                .SendAsync(userPrompt, settings.Model, settings.Token, onChunk, cancellationToken);
        }
    }
}
