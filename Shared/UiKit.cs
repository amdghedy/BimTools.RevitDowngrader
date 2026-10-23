using System;
using System.Drawing;
using System.Windows.Forms;

namespace Downgrader.RevitShared
{
    /// <summary>Small WinForms helpers shared by all dialogs (pure WinForms: identical on .NET Framework 4.7/4.8, .NET 8 and .NET 10).</summary>
    internal static class Ui
    {
        public static void StyleForm(Form f, string title, int w, int h)
        {
            f.AutoScaleMode = AutoScaleMode.Dpi;
            f.Font = SystemFonts.MessageBoxFont;
            f.Text = title;
            f.StartPosition = FormStartPosition.CenterScreen;
            f.ClientSize = new Size(w, h);
            f.ShowIcon = false;
            f.ShowInTaskbar = false;
            f.MinimizeBox = false;
        }

        public static CheckBox Check(string text, bool value, Action<bool> set, ToolTip tt = null, string tip = null)
        {
            var c = new CheckBox { Text = text, AutoSize = true, Checked = value, Margin = new Padding(3) };
            c.CheckedChanged += (s, e) => set(c.Checked);
            if (tt != null && tip != null) tt.SetToolTip(c, tip);
            return c;
        }

        public static NumericUpDown Num(double value, double min, double max, double step, int decimals, Action<double> set)
        {
            var n = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step, DecimalPlaces = decimals, Width = 80 };
            decimal v = (decimal)value;
            n.Value = v < n.Minimum ? n.Minimum : (v > n.Maximum ? n.Maximum : v);
            n.ValueChanged += (s, e) => set((double)n.Value);
            return n;
        }

        public static Button Btn(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(88, 28), Margin = new Padding(3) };
            if (click != null) b.Click += click;
            return b;
        }

        public static Label Lbl(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 3) };
        }

        public static Label Hint(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(640, 0), Margin = new Padding(3, 0, 3, 8) };
        }

        public static FlowLayoutPanel Page()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        }

        public static FlowLayoutPanel Line(params Control[] controls)
        {
            var f = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            f.Controls.AddRange(controls);
            return f;
        }

        public static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "project";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }
    }
}
