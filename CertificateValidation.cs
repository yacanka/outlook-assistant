using System;
using System.Threading;
using System.Threading.Tasks;

namespace Askai
{
    /// <summary>
    /// Native chain builds cannot be interrupted reliably. Bound caller latency and keep
    /// one native job alive at a time, even after its UI/request has been cancelled.
    /// </summary>
    internal static class CertificateValidation
    {
        private static readonly SemaphoreSlim worker = new SemaphoreSlim(1, 1);

        internal static async Task<T> RunAsync<T>(Func<T> validate, CancellationToken cancellationToken)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<T> work = RunSerializedAsync(validate, deadline.Token);
                using (deadline.Token.Register(() => cancelled.TrySetResult(true)))
                {
                    try
                    {
                        if (await Task.WhenAny(work, cancelled.Task).ConfigureAwait(false) != work)
                        {
                            _ = work.ContinueWith(task => { var error = task.Exception; },
                                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                            deadline.Token.ThrowIfCancellationRequested();
                        }
                        return await work.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new AiServiceException("Sertifika kontrolü 30 saniyede tamamlanamadı. Ağ/proxy ve sertifika zincirini kontrol edip yeniden deneyin.");
                    }
                }
            }
        }

        private static async Task<T> RunSerializedAsync<T>(Func<T> validate, CancellationToken token)
        {
            await worker.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    T result = validate();
                    token.ThrowIfCancellationRequested();
                    return result;
                }).ConfigureAwait(false);
            }
            finally { worker.Release(); }
        }
    }
}
