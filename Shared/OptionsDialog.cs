using System.Windows.Forms;
using Downgrader.Core.Util;

namespace Downgrader.RevitShared
{
    /// <summary>Stand-alone options window (tabs with presets, documentation switches and the full property list).</summary>
    public static class OptionsDialog
    {
        public static bool Show(DowngradeOptions options, string title, bool forImport = false)
        {
            using (var form = new OptionsForm(options, title, forImport)) return form.ShowDialog() == DialogResult.OK;
        }
    }

    internal class OptionsForm : Form
    {
        readonly DowngradeOptions _o;
        readonly bool _forImport;
        readonly ToolTip _tt = new ToolTip();
        readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };

        public OptionsForm(DowngradeOptions o, string title, bool forImport)
        {
            _o = o;
            _forImport = forImport;
            Ui.StyleForm(this, title, 640, 640);
            Build();
            var ok = Ui.Btn("OK", null);
            ok.DialogResult = DialogResult.OK;
            var cancel = Ui.Btn("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6) };
            bar.Controls.Add(cancel);
            bar.Controls.Add(ok);
            Controls.Add(_tabs);
            Controls.Add(bar);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        void Build()
        {
            int sel = _tabs.SelectedIndex;
            _tabs.TabPages.Clear();
            foreach (TabPage p in OptionsPages.Create(_o, _forImport, _tt, Build)) _tabs.TabPages.Add(p);
            if (sel >= 0 && sel < _tabs.TabPages.Count) _tabs.SelectedIndex = sel;
        }
    }
}
