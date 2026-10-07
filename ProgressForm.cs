using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Ask.ai
{
    public partial class ProgressForm : Form
    {
        private readonly CancellationTokenSource _cts;
        private Label lblTitle;
        private Label lblStatus;
        private TextBox txtPreview;
        private ProgressBar progressBar;
        private Button btnCancel;
        private int _charCount;

        public ProgressForm(CancellationTokenSource cts)
        {
            _cts = cts;
            BuildUI();
        }

        private void BuildUI()
        {
            // ── Form ayarları ──
            this.Text = "AI Yanıt Asistanı";
            this.Size = new Size(600, 480);
            this.MinimumSize = new Size(400, 350);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.TopMost = true;
            this.ShowInTaskbar = true;
            this.Icon = SystemIcons.Information;
            this.BackColor = Color.White;
            this.Font = new Font("Segoe UI", 9.5f);

            // ── Başlık ──
            lblTitle = new Label
            {
                Text = "Yapay Zeka Yanıtı Hazırlanıyor",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                Location = new Point(20, 15),
                AutoSize = true
            };

            // ── Durum çubuğu ──
            progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Location = new Point(20, 55),
                Size = new Size(540, 8),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // ── Durum metni ──
            lblStatus = new Label
            {
                Text = "API'ye bağlanılıyor...",
                ForeColor = Color.FromArgb(100, 100, 100),
                Location = new Point(20, 72),
                Size = new Size(540, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            // ── Önizleme kutusu ──
            var lblPreview = new Label
            {
                Text = "Yanıt Önizleme:",
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 60, 60),
                Location = new Point(20, 100),
                AutoSize = true
            };

            txtPreview = new TextBox
            {
                Location = new Point(20, 122),
                Size = new Size(540, 260),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(248, 248, 248),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10f),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom
                       | AnchorStyles.Left | AnchorStyles.Right
            };

            // ── İptal butonu ──
            btnCancel = new Button
            {
                Text = "İptal",
                Size = new Size(100, 35),
                Location = new Point(460, 395),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(220, 53, 69),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) =>
            {
                _cts?.Cancel();
                this.Close();
            };

            // ── Kontrolleri ekle ──
            this.Controls.AddRange(new Control[]
            {
                lblTitle, progressBar, lblStatus,
                lblPreview, txtPreview, btnCancel
            });
        }

        /// <summary>
        /// Önizleme kutusuna metin ekler (thread-safe).
        /// </summary>
        public void AppendPreviewText(string text)
        {
            if (this.IsDisposed) return;

            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action<string>(AppendPreviewText), text); }
                catch (ObjectDisposedException) { }
                return;
            }

            // Windows TextBox sadece \r\n ile satır sonu gösterir
            // Gelen metindeki tüm newline varyasyonlarını \r\n'e normalize et
            string normalized = text
                .Replace("\r\n", "\n")   // önce \r\n'leri \n yap (çift dönüşüm olmasın)
                .Replace("\r", "\n")     // yalnız \r varsa onu da \n yap
                .Replace("\n", "\r\n");  // tüm \n'leri \r\n yap

            txtPreview.AppendText(normalized);
            _charCount += text.Length;
            lblStatus.Text = $"Yanıt alınıyor... ({_charCount} karakter)";
        }

        /// <summary>
        /// Bağlantı kuruldu durumunu gösterir.
        /// </summary>
        public void SetConnected()
        {
            if (this.IsDisposed) return;

            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(SetConnected)); }
                catch (ObjectDisposedException) { }
                return;
            }

            lblStatus.Text = "Bağlantı kuruldu, yanıt bekleniyor...";
        }

        /// <summary>
        /// Tamamlandı durumunu gösterir.
        /// </summary>
        public void SetCompleted()
        {
            if (this.IsDisposed) return;

            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(new Action(SetCompleted)); }
                catch (ObjectDisposedException) { }
                return;
            }

            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 100;
            lblStatus.Text = $"✅ Yanıt tamamlandı! ({_charCount} karakter) - Mail oluşturuluyor...";
            lblStatus.ForeColor = Color.FromArgb(40, 167, 69);
            lblTitle.Text = "✅  Yanıt Hazır";
            lblTitle.ForeColor = Color.FromArgb(40, 167, 69);
            btnCancel.Enabled = false;
        }

        /// <summary>
        /// Form kapatılırken iptal işlemini tetikle.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_cts.IsCancellationRequested)
                _cts.Cancel();
            base.OnFormClosing(e);
        }

        private void ProgressForm_Load(object sender, EventArgs e)
        {

        }
    }
}
