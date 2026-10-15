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

        internal async Task<string> SendStreamingRequestAsync(string userPrompt, Action<string> onChunk,
            CancellationToken cancellationToken, AiSettings settings)
        {
            bool legacy = settings.Provider == AiProvider.Legacy;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(AppConfig.TimeoutMinutes));
                try
                {
                    var client = await AiHttpTransport.GetClientAsync(settings.UseTls12, legacy,
                        legacy ? settings.ActiveLegacyCertificate : settings.ActiveCentralCertificate,
                        legacy ? settings.LegacyCertificateTrustMode : settings.CentralCertificateTrustMode, deadline.Token).ConfigureAwait(false);
                    if (legacy)
                        return await new LegacyAiService(client).SendStreamingRequestAsync(userPrompt, onChunk, deadline.Token).ConfigureAwait(false);
                    return await new CentralAiService(client).SendAsync(userPrompt, settings.Model, settings.Token, onChunk, deadline.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("AI isteği zaman aşımına uğradı.");
                }
            }
        }
    }
}
