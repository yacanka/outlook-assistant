using System;
using System.Drawing;
using System.Windows.Forms;

namespace Ask.ai
{
    // All columns use percentages. Wrap AutoSize content against the viewport, not its preferred width.
    internal sealed class ResponsiveTableLayoutPanel : TableLayoutPanel
    {
        private bool arranging;

        public ResponsiveTableLayoutPanel()
        {
            Dock = DockStyle.Fill;
            AutoScroll = true;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (arranging) return;
            arranging = true;
            try
            {
                ConstrainWidths();
                base.OnLayout(e);
                // A vertical scrollbar can reduce the available width during the first pass.
                if (ConstrainWidths()) base.OnLayout(e);
            }
            finally { arranging = false; }
        }

        private bool ConstrainWidths()
        {
            int availableWidth = Math.Max(1, ClientSize.Width - Padding.Horizontal);
            bool changed = false;
            foreach (Control control in Controls)
            {
                if (!(control is Label) && !(control is FlowLayoutPanel) && !(control is CheckBox)) continue;
                int column = GetColumn(control);
                if (column < 0 || column >= ColumnStyles.Count) continue;
                int width = 0;
                int end = Math.Min(ColumnStyles.Count, column + GetColumnSpan(control));
                for (int index = column; index < end; index++)
                    width += (int)(availableWidth * ColumnStyles[index].Width / 100f);
                var maximum = new Size(Math.Max(1, width - control.Margin.Horizontal), 0);
                if (control.MaximumSize == maximum) continue;
                control.MaximumSize = maximum;
                changed = true;
            }
            return changed;
        }
    }
}
