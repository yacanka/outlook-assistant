using System;
using System.Drawing;
using System.Windows.Forms;

namespace Ask.ai
{
    internal sealed class MailInputForm : Form
    {
        private readonly TextBox subject = new TextBox { Dock = DockStyle.Fill, MaxLength = 255 };
        private readonly TextBox instructions = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000, AcceptsReturn = true
        };
        private readonly Label error = new Label { AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.Firebrick };
        public string MailSubject => subject.Text.Trim();
        public string Instructions => instructions.Text.Trim();

        public MailInputForm(bool composing, bool replyAll, string sourceSubject)
        {
            Text = composing ? "AI ile mail hazırla" : replyAll ? "AI ile tümünü yanıtla" : "AI ile yanıtla";
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 370);
            MinimumSize = new Size(500, 370);
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            subject.Text = sourceSubject;
            subject.ReadOnly = !composing;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = composing ? "&Konu (zorunlu)" : "Kaynak mailin konusu", AutoSize = true }, 0, 0);
            layout.Controls.Add(subject, 0, 1);
            layout.Controls.Add(new Label
            {
                Text = composing ? "&Açıklama / talimat (isteğe bağlı)" : "&Yanıtta ne söylemek istiyorsunuz? (isteğe bağlı)",
                AutoSize = true, Padding = new Padding(0, 12, 0, 0)
            }, 0, 2);
            layout.Controls.Add(instructions, 0, 3);
            layout.Controls.Add(error, 0, 4);
            layout.Controls.Add(new Label
            {
                Text = composing
                    ? "Onayınızdan sonra yeni mail açılacak. Alıcıları Outlook’ta siz belirleyeceksiniz."
                    : "Boş bırakırsanız kaynak mailin bağlamına göre yanıt hazırlanır. Aktarım için ayrıca onayınız alınır.",
                AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 8)
            }, 0, 5);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "&Vazgeç", AutoSize = true, DialogResult = DialogResult.Cancel };
            var prepare = new Button { Text = "&Hazırla", AutoSize = true };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(prepare);
            layout.Controls.Add(buttons, 0, 6);
            Controls.Add(layout);
            CancelButton = cancel;
            prepare.Click += (sender, args) =>
            {
                if (composing && string.IsNullOrWhiteSpace(MailSubject))
                {
                    error.Text = "Mail konusunu girin.";
                    subject.Focus();
                    return;
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            Shown += (sender, args) => { if (composing) subject.Focus(); else instructions.Focus(); };
        }
    }
}
