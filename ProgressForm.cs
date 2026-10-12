using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Askai;

namespace Ask.ai
{
    public partial class ProgressForm : Form
    {
        private readonly CancellationTokenSource cancellation;
        private readonly TaskCompletionSource<bool> approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Label status = new Label { AutoSize = true, Dock = DockStyle.Fill };
        private readonly Label action = new Label { AutoSize = true, Dock = DockStyle.Fill };
        private readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee };
        private readonly Button apply = new Button { Text = "&Mail’e aktar", AutoSize = true, Enabled = false };
        private readonly Button cancel = new Button { Text = "&İptal", AutoSize = true };
        private readonly System.Windows.Forms.Timer statusTimer;
        private readonly RichTextBox preview = new RichTextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, DetectUrls = false,
            Dock = DockStyle.Fill, BackColor = SystemColors.Window, Height = 180,
            MinimumSize = new Size(0, 150), MaxLength = 1024 * 1024, TabStop = true
        };
        private readonly object chunkLock = new object();
        private readonly StringBuilder pendingText = new StringBuilder();
        private readonly MailPreviewBuffer previewBuffer = new MailPreviewBuffer();
        private readonly bool applyMarkdown;
        private readonly bool supportsRichText;
        private bool previewFailed;
        private int receivedCharacters;
        private bool finished;
        private bool applying;

        public ProgressForm(CancellationTokenSource cancellation, string title, string approvalMessage, bool applyMarkdown = false, bool supportsRichText = true)
        {
            this.applyMarkdown = applyMarkdown;
            this.supportsRichText = supportsRichText;
            this.cancellation = cancellation;
            components = new Container();
            Text = Askai.AiBrand.Name;
            Icon = AssistantIcon.WindowIcon;
            Font = new Font("Segoe UI", 10f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 500);
            MinimumSize = SizeFromClientSize(new Size(400, 200));
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            var layout = new ResponsiveTableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = title, AutoSize = true, Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            layout.Controls.Add(progress, 0, 1);
            status.Text = "İstek hazırlanıyor; servisten içerik bekleniyor…";
            layout.Controls.Add(status, 0, 2);
            action.Text = approvalMessage + (applyMarkdown && !supportsRichText
                ? "\nHedef mail düz metin biçiminde. Markdown okunabilir metne dönüştürülecek; kalın yazı ve tablo biçimlendirmesi uygulanamayacak." : "");
            layout.Controls.Add(preview, 0, 3);
            layout.Controls.Add(action, 0, 4);
            layout.Controls.Add(new Label
            {
                Text = "Uygulama mail göndermez. Aktarımdan sonra metni Outlook’ta inceleyip düzenleyebilirsiniz.",
                AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8)
            }, 0, 5);
            var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(apply);
            layout.Controls.Add(buttons, 0, 6);
            Controls.Add(layout);
            CancelButton = cancel;
            cancel.Click += (sender, args) => Close();
            apply.Click += (sender, args) =>
            {
                if (!apply.Enabled || cancellation.IsCancellationRequested) return;
                apply.Enabled = false;
                cancel.Enabled = false;
                applying = true;
                status.Text = "Onay alındı; içerik Outlook’a aktarılıyor…";
                approval.TrySetResult(true);
            };
            statusTimer = new System.Windows.Forms.Timer(components) { Interval = 150 };
            statusTimer.Tick += (sender, args) =>
            {
                try { FlushPreview(); }
                catch (Exception)
                {
                    // WinForms timer exceptions must not escape the UI message loop.
                    // Never allow approval after a failed preview.
                    FailPreview();
                    return;
                }
                int count = receivedCharacters;
                if (count > 0) status.Text = "İçerik hazırlanıyor… (" + count + " karakter alındı)";
            };
            statusTimer.Start();
            preview.SizeChanged += (sender, args) =>
            {
                if (!applyMarkdown || !supportsRichText || previewFailed || previewBuffer.Source.Length == 0) return;
                try { RenderPreview(); }
                catch (Exception) { FailPreview(); }
            };
        }

        private void FailPreview()
        {
            previewFailed = true;
            statusTimer.Stop();
            apply.Enabled = false;
            status.Text = "Önizleme biçimlendirilemedi. İçerik aktarılmayacak.";
        }

        // Transport callbacks only queue text; the UI timer batches updates on the UI thread.
        public void ReportChunk(string chunk)
        {
            if (string.IsNullOrEmpty(chunk)) return;
            lock (chunkLock) pendingText.Append(chunk);
        }

        private void FlushPreview()
        {
            string text;
            lock (chunkLock)
            {
                if (pendingText.Length == 0) return;
                text = pendingText.ToString();
                pendingText.Clear();
            }
            receivedCharacters += text.Length;
            previewBuffer.Append(text);
            RenderPreview();
        }

        private void RenderPreview()
        {
            int selectionStart = preview.SelectionStart;
            int selectionLength = preview.SelectionLength;
            var scrollPosition = new Point();
            SendMessage(preview.Handle, 0x04DD /* EM_GETSCROLLPOS */, IntPtr.Zero, ref scrollPosition);
            Point endPosition = preview.GetPositionFromCharIndex(preview.TextLength);
            bool followEnd = selectionStart + selectionLength >= preview.TextLength
                && (preview.TextLength == 0 || (endPosition.Y >= 0 && endPosition.Y < preview.ClientSize.Height));
            if (applyMarkdown)
            {
                int tableWidth = Math.Max(1440, (preview.ClientSize.Width - 16) * 1440 / preview.DeviceDpi);
                var content = MailFormatting.Create(previewBuffer.Source, true, tableWidth);
                if (supportsRichText) preview.Rtf = content.Rtf;
                else preview.Text = MailText.ForPreview(content.PlainText);
            }
            else preview.Text = MailText.ForPreview(previewBuffer.Source);
            if (followEnd)
            {
                preview.Select(preview.TextLength, 0);
                preview.ScrollToCaret();
            }
            else
            {
                preview.Select(Math.Min(selectionStart, preview.TextLength), Math.Min(selectionLength, Math.Max(0, preview.TextLength - selectionStart)));
                SendMessage(preview.Handle, 0x04DE /* EM_SETSCROLLPOS */, IntPtr.Zero, ref scrollPosition);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, ref Point point);

        public void SetReady() { SetReady(null); }

        public void SetReady(string result)
        {
            if (IsDisposed || cancellation.IsCancellationRequested) return;
            statusTimer.Stop();
            if (previewFailed) throw new MailActionException("Önizleme biçimlendirilemedi. Mail değiştirilmedi.");
            FlushPreview();
            if (applyMarkdown) MailFormatting.Create(result ?? previewBuffer.Source, true).EnsureVisibleText();
            // The completed provider result is authoritative, even if callbacks omitted a final fragment.
            if (result != null)
            {
                previewBuffer.Replace(result);
                RenderPreview();
            }
            progress.Style = ProgressBarStyle.Continuous;
            progress.Value = 100;
            status.Text = "İçerik hazır. Mail’e aktarmak için onayınız bekleniyor.";
            cancel.Text = "&Vazgeç";
            apply.Enabled = true;
            // No default AcceptButton: finishing generation must not turn an accidental Enter into approval.
        }

        public Task<bool> WaitForApprovalAsync() { return approval.Task; }

        public void FinishAndClose()
        {
            finished = true;
            if (!IsDisposed) Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (applying && !finished && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            statusTimer.Stop();
            if (!finished && !cancellation.IsCancellationRequested) cancellation.Cancel();
            approval.TrySetResult(false);
            base.OnFormClosing(e);
        }

        private void ProgressForm_Load(object sender, EventArgs e) { }
    }
}
