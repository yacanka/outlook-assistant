using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Askai;

namespace Ask.ai
{
    internal sealed class SkillLibraryControl : UserControl
    {
        private readonly ListBox list = new ListBox { Name = "SkillList", Dock = DockStyle.Fill, Height = 150 };
        internal List<AiSkill> Skills
        {
            get
            {
                var result = new List<AiSkill>();
                foreach (AiSkill skill in list.Items) result.Add(skill);
                return result;
            }
            set { list.Items.Clear(); if (value != null) foreach (var skill in value) list.Items.Add(skill); }
        }

        internal SkillLibraryControl()
        {
            Dock = DockStyle.Fill;
            var layout = new ResponsiveTableLayoutPanel { ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = "Sık kullandığınız talimatları kaydedin. Seçilen beceri AI servisine iletilir. Değişiklikler Ayarlar → Kaydet ile saklanır.", AutoSize = true, Dock = DockStyle.Fill }, 0, 0);
            layout.Controls.Add(list, 0, 1);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var add = new Button { Text = "Ekle", AutoSize = true };
            var edit = new Button { Text = "Düzenle", AutoSize = true };
            var remove = new Button { Text = "Sil", AutoSize = true };
            buttons.Controls.AddRange(new Control[] { add, edit, remove });
            add.Click += (s, e) => EditSkill(false);
            edit.Click += (s, e) => EditSkill(true);
            remove.Click += (s, e) => { if (list.SelectedIndex >= 0) list.Items.RemoveAt(list.SelectedIndex); };
            list.SelectedIndexChanged += (s, e) => { edit.Enabled = remove.Enabled = list.SelectedIndex >= 0; };
            edit.Enabled = remove.Enabled = false;
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);
        }

        private void EditSkill(bool editing)
        {
            int index = editing ? list.SelectedIndex : -1;
            if (editing && index < 0) return;
            if (!editing && list.Items.Count >= 30)
            {
                MessageBox.Show(this, "En fazla 30 beceri kaydedilebilir.", AiBrand.Name);
                return;
            }
            using (var dialog = new SkillEditForm(index < 0 ? null : (AiSkill)list.Items[index]))
            {
                while (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    var candidate = Skills;
                    if (index < 0) candidate.Add(dialog.Skill); else candidate[index] = dialog.Skill;
                    try { AiSkillCatalog.Serialize(candidate); }
                    catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, AiBrand.Name); continue; }
                    Skills = candidate;
                    list.SelectedIndex = index < 0 ? list.Items.Count - 1 : index;
                    return;
                }
            }
        }
    }

    internal sealed class SkillEditForm : Form
    {
        internal AiSkill Skill { get; private set; }
        internal SkillEditForm(AiSkill skill)
        {
            Text = skill == null ? "Beceri ekle" : "Beceriyi düzenle";
            Icon = AssistantIcon.WindowIcon;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(500, 360);
            MinimumSize = SizeFromClientSize(new Size(360, 240));
            StartPosition = FormStartPosition.CenterParent;
            var name = new TextBox { Name = "SkillName", Text = skill?.Name ?? "", MaxLength = 80, Dock = DockStyle.Fill };
            var text = new TextBox { Name = "SkillInstructions", Text = skill?.Instructions ?? "", MaxLength = 4000, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, MinimumSize = new Size(0, 120) };
            var layout = new ResponsiveTableLayoutPanel { ColumnCount = 1, RowCount = 5, Padding = new Padding(16) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
            layout.Controls.Add(new Label { Text = "Beceri adı", AutoSize = true }, 0, 0);
            layout.Controls.Add(name, 0, 1);
            layout.Controls.Add(new Label { Text = "Talimat", AutoSize = true }, 0, 2);
            layout.Controls.Add(text, 0, 3);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, AutoSize = true };
            var save = new Button { Text = "Uygula", AutoSize = true };
            save.Click += (s, e) =>
            {
                try { Skill = new AiSkill(name.Text, text.Text); DialogResult = DialogResult.OK; }
                catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, AiBrand.Name); }
            };
            buttons.Controls.AddRange(new Control[] { cancel, save });
            layout.Controls.Add(buttons, 0, 4);
            Controls.Add(layout);
            CancelButton = cancel;
        }
    }
}
