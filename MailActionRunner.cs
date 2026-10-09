using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Askai;

namespace Ask.ai
{
    internal static class MailActionRunner
    {
        private static int running;

        public static async Task RunAsync(MailCapability capability, object windowContext)
        {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
            {
                MessageBox.Show("Önce devam eden AI işlemini tamamlayın veya iptal edin.", "AI Mail Asistanı");
                return;
            }
            try
            {
                using (var target = OutlookMailTarget.Capture(Globals.ThisAddIn.Application, windowContext, capability))
                {
                    var request = ReadRequest(capability, target);
                    if (request == null) return;
                    await PrepareAndApplyAsync(request, target);
                }
            }
            catch (MailActionException ex) { ShowError(ex.Message); }
            catch (AiServiceException ex) { ShowError(ex.Message); }
            catch (AiResponseException ex) { ShowError(ex.Message); }
            catch (Exception)
            {
                // Never surface COM details, provider response bodies or credentials.
                ShowError("Mail işlemi tamamlanamadı. Outlook penceresini ve AI ayarlarınızı kontrol edip yeniden deneyin. Açılan taslak varsa içeriğini kontrol edin.");
            }
            finally { Volatile.Write(ref running, 0); }
        }

        private static MailGenerationRequest ReadRequest(MailCapability capability, OutlookMailTarget target)
        {
            if (capability == MailCapability.Improve)
                return new MailGenerationRequest(capability, target.Subject, target.Source, "");
            bool composing = capability == MailCapability.Compose;
            using (var input = new MailInputForm(composing, capability == MailCapability.ReplyAll, target.Subject))
            {
                if (input.ShowDialog() != DialogResult.OK) return null;
                if (composing) target.SetNewSubject(input.MailSubject);
                return new MailGenerationRequest(capability, target.Subject, target.Source, input.Instructions);
            }
        }

        private static async Task PrepareAndApplyAsync(MailGenerationRequest request, IAiDraftTarget target)
        {
            string prompt = request.BuildPrompt();
            var session = new MailGenerationSession();
            using (var cancellation = new CancellationTokenSource())
            using (var progress = new ProgressForm(cancellation, GetTitle(request.Capability), GetApprovalMessage(request.Capability)))
            {
                progress.Show();
                try
                {
                    string result = await new AiService().SendStreamingRequestAsync(prompt, progress.ReportChunk, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (!session.Complete(result)) return;
                    progress.SetReady();
                    if (!await progress.WaitForApprovalAsync()) { session.Cancel(); return; }
                    cancellation.Token.ThrowIfCancellationRequested();
                    session.ApplyApproved(target);
                }
                catch (OperationCanceledException) { session.Cancel(); }
                catch (TimeoutException)
                {
                    session.Fail();
                    throw new MailActionException("AI isteği zaman aşımına uğradı. İçerik aktarılmadı; yeniden deneyebilirsiniz.");
                }
                catch { session.Fail(); throw; }
                finally { progress.FinishAndClose(); }
            }
        }

        private static string GetTitle(MailCapability capability)
        {
            if (capability == MailCapability.Compose) return "Yeni mail hazırlanıyor";
            if (capability == MailCapability.Improve) return "Seçili metin iyileştiriliyor";
            return "Mail yanıtı hazırlanıyor";
        }

        private static string GetApprovalMessage(MailCapability capability)
        {
            switch (capability)
            {
                case MailCapability.Compose: return "Onayınızla yeni bir Outlook maili açılacak ve hazırlanan içerik imzanızın önüne eklenecek.";
                case MailCapability.Improve: return "Onayınızla başlangıçta seçtiğiniz metin değiştirilecek. Seçimin dışındaki imza ve yazışmalar korunacak.";
                case MailCapability.ReplyAll: return "Onayınızla Outlook’un tümünü yanıtla taslağı açılacak. Göndermeden önce alıcıları ve metni kontrol edin.";
                default: return "Onayınızla Outlook yanıt taslağı açılacak ve içerik imza ile önceki yazışmanın önüne eklenecek.";
            }
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(message, "AI Mail Asistanı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
