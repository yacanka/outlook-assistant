using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Ask.ai;

internal static class PopupLayoutTests
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        using (var input = new MailInputForm(false, true, "Örnek konu"))
        {
            CheckSizes(input);
            input.Scale(new SizeF(1.5f, 1.5f));
            CheckSizes(input);
        }
        using (var cancellation = new CancellationTokenSource())
        using (var progress = new ProgressForm(cancellation, "Mail yanıtı hazırlanıyor",
            "Onayınızla Outlook’un tümünü yanıtla taslağı açılacak. Göndermeden önce alıcıları ve metni kontrol edin."))
        {
            CheckSizes(progress);
            progress.SetReady();
            CheckContent(progress);
            progress.Scale(new SizeF(1.5f, 1.5f));
            CheckSizes(progress);
            progress.FinishAndClose();
        }
        using (var input = new MailInputForm(true, false, ""))
        {
            input.Show();
            FindButton(input, "&Hazırla").PerformClick();
            Check(input.DialogResult != DialogResult.OK, "Empty subject must still require validation.");
            CheckSizes(input);
        }
        CheckSettingsColumns();
        Console.WriteLine("All popup layout checks passed.");
    }

    private static void CheckSettingsColumns()
    {
        using (var form = new Form { ClientSize = new Size(560, 640) })
        {
            var layout = new ResponsiveTableLayoutPanel { ColumnCount = 2, RowCount = 3, Padding = new Padding(16) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            for (int row = 0; row < 3; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = "Central sertifika modu", AutoSize = true }, 0, 0);
            layout.Controls.Add(new ComboBox { Dock = DockStyle.Fill }, 1, 0);
            var help = new Label { Text = new string('W', 300), AutoSize = true, Dock = DockStyle.Fill };
            layout.Controls.Add(help, 1, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            buttons.Controls.Add(new Button { Text = "Sertifika seç…", AutoSize = true });
            buttons.Controls.Add(new Button { Text = "Temizle", AutoSize = true });
            layout.Controls.Add(buttons, 1, 2);
            form.Controls.Add(layout);
            CheckSizes(form);
            help.Text = "Kısa açıklama";
            CheckContent(form);
            help.Text = new string('W', 300);
            CheckContent(form);
        }
    }

    private static void CheckSizes(Form form)
    {
        form.Show();
        var initial = form.ClientSize;
        foreach (var size in new[] { initial, new Size(800, 600), new Size(440, 240), initial })
        {
            form.ClientSize = size;
            CheckContent(form);
        }
    }

    private static void CheckContent(Control parent)
    {
        parent.PerformLayout();
        Application.DoEvents();
        foreach (Control control in parent.Controls)
        {
            var label = control as Label;
            if (label != null && label.Text.Length > 0)
            {
                var preferred = label.GetPreferredSize(new Size(label.Width, 0));
                Check(label.Height >= preferred.Height, "Text clipped vertically: " + label.Text);
            }
            var table = parent as TableLayoutPanel;
            if (table != null)
            {
                var position = table.GetPositionFromControl(control);
                int available = table.GetColumnWidths()[position.Column];
                Check(control.Width + control.Margin.Horizontal <= available,
                    "Control overflows its column: " + control.GetType().Name);
                Check(control.Bottom + control.Margin.Bottom <= table.DisplayRectangle.Bottom,
                    "Content must be reachable through vertical scrolling.");
            }
            CheckContent(control);
        }
        var scrollable = parent as ScrollableControl;
        if (scrollable != null)
            Check(!scrollable.HorizontalScroll.Visible, "Popup must not require horizontal scrolling.");
    }

    private static Button FindButton(Control parent, string text)
    {
        foreach (Control control in parent.Controls)
        {
            var button = control as Button;
            if (button != null && button.Text == text) return button;
            var found = FindButton(control, text);
            if (found != null) return found;
        }
        return null;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
