using System;
using System.Drawing;
using System.Windows.Forms;
using Downgrader.Core.Util;

namespace Downgrader.RevitShared
{
    /// <summary>Modeless progress window with Cancel. Updated from inside the Revit command, so it pumps messages itself.</summary>
    internal class ProgressForm : Form
    {
        readonly Label _stage = new Label { AutoSize = true, Text = "Starting..." };
        readonly Label _count = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = " " };
        readonly ProgressBar _bar = new ProgressBar { Width = 480, Height = 22, Style = ProgressBarStyle.Marquee };
        readonly DateTime _start = DateTime.Now;
        public bool CancelRequested { get; private set; }

        public ProgressForm(string title)
        {
            Ui.StyleForm(this, title, 520, 160);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            TopMost = true;
            var cancel = Ui.Btn("Cancel", null);
            cancel.Click += (s, e) => { CancelRequested = true; cancel.Enabled = false; _stage.Text = "Cancelling..."; };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14) };
            flow.Controls.Add(_stage);
            flow.Controls.Add(_bar);
            flow.Controls.Add(_count);
            flow.Controls.Add(cancel);
            Controls.Add(flow);
        }

        public void UpdateProgress(string stage, int current, int total)
        {
            if (!CancelRequested) _stage.Text = stage;
            TimeSpan t = DateTime.Now - _start;
            string elapsed = ((int)t.TotalMinutes).ToString("00") + ":" + t.Seconds.ToString("00");
            if (total > 0)
            {
                _bar.Style = ProgressBarStyle.Continuous;
                _bar.Maximum = total;
                _bar.Value = Math.Max(0, Math.Min(current, total));
                _count.Text = current + " / " + total + "      elapsed " + elapsed;
            }
            else
            {
                _bar.Style = ProgressBarStyle.Marquee;
                _count.Text = "elapsed " + elapsed;
            }
            Application.DoEvents();
        }
    }
}
