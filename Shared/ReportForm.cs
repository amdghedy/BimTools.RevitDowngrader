using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Downgrader.Core.Util;

namespace Downgrader.RevitShared
{
    /// <summary>Filterable result window: tier counts, search, colour-coded rows.</summary>
    internal class ReportForm : Form
    {
        readonly ConversionReport _report;
        readonly string _folder;
        readonly DataGridView _grid = new DataGridView();
        readonly ComboBox _tier = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        readonly TextBox _q = new TextBox { Width = 260 };
        readonly Label _status = new Label { AutoSize = true, Margin = new Padding(12, 6, 3, 3) };

        static Color TierColor(string tier)
        {
            switch (tier)
            {
                case Tiers.Native: return Color.FromArgb(230, 244, 234);
                case Tiers.NativeWithLoss: return Color.FromArgb(255, 244, 214);
                case Tiers.DirectShape: return Color.FromArgb(232, 240, 254);
                case Tiers.Skipped: return Color.FromArgb(253, 231, 233);
            }
            return Color.White;
        }

        public ReportForm(ConversionReport report, string folder, string title)
        {
            _report = report;
            _folder = folder;
            Ui.StyleForm(this, title, 1040, 640);
            ShowInTaskbar = true;
            MinimizeBox = true;
            MaximizeBox = true;

            // ---- tier summary strip
            var strip = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 0) };
            foreach (string t in new[] { Tiers.Native, Tiers.NativeWithLoss, Tiers.DirectShape, Tiers.Skipped })
            {
                int n = report.Entries.Count(e => e.Tier == t);
                strip.Controls.Add(new Label { Text = "  " + t + ":  " + n + "  ", AutoSize = true, BackColor = TierColor(t), Margin = new Padding(3, 3, 12, 3), Padding = new Padding(4) });
            }

            // ---- filter bar
            _tier.Items.AddRange(new object[] { "All", Tiers.Native, Tiers.NativeWithLoss, Tiers.DirectShape, Tiers.Skipped });
            _tier.SelectedIndex = 0;
            _tier.SelectedIndexChanged += (s, e) => ApplyFilter();
            _q.TextChanged += (s, e) => ApplyFilter();
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
            bar.Controls.Add(Ui.Lbl("Show:"));
            bar.Controls.Add(_tier);
            bar.Controls.Add(Ui.Lbl("Search:"));
            bar.Controls.Add(_q);
            bar.Controls.Add(_status);

            // ---- grid
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AutoGenerateColumns = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.BackgroundColor = SystemColors.Window;
            AddCol("Stage", 110, false);
            AddCol("Tier", 110, false);
            AddCol("Category", 140, false);
            AddCol("Name", 260, false);
            AddCol("Reason", 300, true);
            _grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
                var item = _grid.Rows[e.RowIndex].DataBoundItem as ReportEntry;
                if (item != null) e.CellStyle.BackColor = TierColor(item.Tier);
            };

            // ---- buttons
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6) };
            var close = Ui.Btn("Close", (s, e) => Close());
            buttons.Controls.Add(close);
            buttons.Controls.Add(Ui.Btn("Copy summary", (s, e) => { try { Clipboard.SetText(_report.SummaryText()); } catch { } }));
            if (!string.IsNullOrEmpty(folder))
            {
                buttons.Controls.Add(Ui.Btn("Open report folder", (s, e) => { try { Process.Start("explorer.exe", "\"" + _folder + "\""); } catch { } }));
                buttons.Controls.Add(Ui.Btn("Open HTML report", (s, e) => OpenHtml()));
            }
            CancelButton = close;

            Controls.Add(_grid);       // fill first, then the docked bars
            Controls.Add(buttons);
            Controls.Add(bar);
            Controls.Add(strip);
            ApplyFilter();
        }

        void AddCol(string prop, int width, bool fill)
        {
            var c = new DataGridViewTextBoxColumn { DataPropertyName = prop, HeaderText = prop, Width = width, SortMode = DataGridViewColumnSortMode.Automatic };
            if (fill) c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _grid.Columns.Add(c);
        }

        void ApplyFilter()
        {
            string tier = _tier.SelectedItem as string;
            string q = _q.Text.Trim().ToLowerInvariant();
            List<ReportEntry> list = _report.Entries.Where(e =>
                (tier == "All" || e.Tier == tier) &&
                (q.Length == 0 || ((e.Stage ?? "") + " " + (e.Category ?? "") + " " + (e.Name ?? "") + " " + (e.Reason ?? "")).ToLowerInvariant().Contains(q))).ToList();
            _grid.DataSource = new BindingList<ReportEntry>(list);
            _status.Text = list.Count + " of " + _report.Entries.Count + " rows";
        }

        void OpenHtml()
        {
            try
            {
                string[] files = System.IO.Directory.GetFiles(_folder, "*.html");
                if (files.Length == 0) return;
                string newest = files.OrderByDescending(f => System.IO.File.GetLastWriteTime(f)).First();
                Process.Start(new ProcessStartInfo(newest) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
