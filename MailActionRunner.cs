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
                MessageBox.Show("Önce devam eden AI işlemini tamamlayın veya iptal edin.", AiBrand.Name);
                return;
            }
            var stage = MailActionStage.Outlook;
            try
            {
                using (var target = OutlookMailTarget.Capture(Globals.ThisAddIn.Application, windowContext, capability))
                {
                    stage = MailActionStage.Settings;
                    var settings = AiSettings.Load();
                    target.ApplyMarkdown = settings.ApplyMarkdown;
                    stage = MailActionStage.Input;
                    await ReadRequestAndPrepareAsync(capability, target, settings, value => stage = value);
                }
            }
            catch (MailActionException ex) { ShowError(ex.Message); }
            catch (AiServiceException ex) { ShowError(ex.Message); }
            catch (AiResponseException ex) { ShowError(ex.Message); }
            catch (Exception ex)
            {
                // Never surface COM details, provider response bodies or credentials.
                ShowError(AiConnectionErrors.UnexpectedMailError(ex, stage));
            }
            finally { Volatile.Write(ref running, 0); }
        }

        private static async Task ReadRequestAndPrepareAsync(MailCapability capability, OutlookMailTarget target, AiSettings settings,
            Action<MailActionStage> setStage)
        {
            if (capability == MailCapability.Improve || capability == MailCapability.Summarize)
            {
                var request = new MailGenerationRequest(capability, target.Subject, target.Source, "", target.ApplyMarkdown);
                await PrepareAndApplyAsync(request, target, settings, setStage);
                return;
            }
            bool composing = capability == MailCapability.Compose;
            using (var input = new MailInputForm(composing, capability == MailCapability.ReplyAll, target.Subject, settings.Skills))
            {
                // Reuse the dialog so Back preserves the exact text and editing state.
                while (input.ShowDialog() == DialogResult.OK)
                {
                    if (composing) target.SetNewSubject(input.MailSubject);
                    var request = new MailGenerationRequest(capability, target.Subject, target.Source, input.Instructions, target.ApplyMarkdown);
                    if (!await PrepareAndApplyAsync(request, target, settings, setStage)) return;
                    setStage(MailActionStage.Input);
                }
            }
        }

        // Returns true only when the user explicitly asks to edit the prompt again.
        private static async Task<bool> PrepareAndApplyAsync(MailGenerationRequest request, OutlookMailTarget target, AiSettings settings,
            Action<MailActionStage> setStage)
        {
            setStage(MailActionStage.Preparing);
            string prompt = request.BuildPrompt(settings.MemoryEnabled ? settings.MemoryText : null);
            var session = new MailGenerationSession();
            using (var cancellation = new CancellationTokenSource())
            using (var progress = new ProgressForm(cancellation, GetTitle(request.Capability), GetApprovalMessage(request.Capability),
                request.ApplyMarkdown, request.Capability == MailCapability.Summarize || target.SupportsRichText,
                allowBack: request.Capability != MailCapability.Improve && request.Capability != MailCapability.Summarize))
            {
                if (request.Capability == MailCapability.Summarize) progress.ConfigureSummary();
                else progress.ConfigureRevisions((current, instruction, token) => new AiService().SendStreamingRequestAsync(
                    request.BuildRevisionPrompt(current, instruction, settings.MemoryEnabled ? settings.MemoryText : null), null, token, settings));
                progress.Show();
                try
                {
                    setStage(MailActionStage.Request);
                    string result = await new AiService().SendStreamingRequestAsync(prompt, progress.ReportChunk, cancellation.Token, settings);
                    cancellation.Token.ThrowIfCancellationRequested();
                    // Initial and revised results remain local until the final explicit transfer.
                    if (string.IsNullOrWhiteSpace(result) || result.Length > MailContentLimits.GeneratedCharacters)
                        throw new MailActionException("Üretilen içerik boş veya çok uzun.");
                    setStage(MailActionStage.Preview);
                    await progress.SetReadyAsync(result);
                    setStage(MailActionStage.Approval);
                    if (!await progress.WaitForApprovalAsync()) { session.Cancel(); return progress.BackRequested; }
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (request.Capability == MailCapability.Summarize) return false;
                    result = progress.SelectedResult;
                    if (!session.Complete(result)) return false;
                    setStage(MailActionStage.Applying);
                    target.SetPreparedContent(result, progress.PreparedContent);
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
            return false;
        }

        private static string GetTitle(MailCapability capability)
        {
            if (capability == MailCapability.Summarize) return "Mail özetleniyor";
            if (capability == MailCapability.Compose) return "Yeni mail hazırlanıyor";
            if (capability == MailCapability.Improve) return "Seçili metin iyileştiriliyor";
            return "Mail yanıtı hazırlanıyor";
        }

        private static string GetApprovalMessage(MailCapability capability)
        {
            switch (capability)
            {
                case MailCapability.Summarize: return "Özet, aksiyonlar ve açık sorular gösterilir. Belirtilmeyen bilgiler tahmin edilmez; kaynak mail değiştirilmez.";
                case MailCapability.Compose: return "Onayınızla yeni bir Outlook maili açılacak ve hazırlanan içerik imzanızın önüne eklenecek.";
                case MailCapability.Improve: return "Onayınızla başlangıçta seçtiğiniz metin değiştirilecek. Seçimin dışındaki imza ve yazışmalar korunacak.";
                case MailCapability.ReplyAll: return "Onayınızla Outlook’un tümünü yanıtla taslağı açılacak. Göndermeden önce alıcıları ve metni kontrol edin.";
                default: return "Onayınızla Outlook yanıt taslağı açılacak ve içerik imza ile önceki yazışmanın önüne eklenecek.";
            }
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(message, AiBrand.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
