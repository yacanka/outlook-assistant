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
            var settings = AiSettings.Load();
            if (settings.Provider == AiProvider.Legacy)
                return new LegacyAiService(AiHttpTransport.GetClient(settings.UseTls12, forLegacy: true))
                    .SendStreamingRequestAsync(userPrompt, onChunk, cancellationToken);
            return new CentralAiService(AiHttpTransport.GetClient(settings.UseTls12,
                    serverCertificateBase64: settings.CentralServerCertificate))
                .SendAsync(userPrompt, settings.Model, settings.Token, onChunk, cancellationToken);
        }
    }
}
