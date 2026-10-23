using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Downgrader.Core.Util;

namespace Downgrader.RevitShared
{
    internal class ExportStudioInput
    {
        public string ProjectTitle;
        public string DefaultFolder;
        public DowngradeOptions Options;
        public List<string> Levels = new List<string>();
        public int SelectedCount;
        public string ActiveViewName;
        public Func<List<CatItem>> ScanCategories;
    }

    internal class ExportStudioResult
    {
        public string PackageRoot;
        public bool Model;
        public bool Docs;
        public bool SaveAsDefaults;
        public DowngradeOptions Options;
    }

    /// <summary>One window for everything that decides what is exported and how.</summary>
    internal class ExportStudioForm : Form
    {
        readonly ExportStudioInput _in;
        readonly DowngradeOptions _o;
        readonly ToolTip _tt = new ToolTip();
        readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };
        readonly List<TabPage> _optPages = new List<TabPage>();
        readonly CategoryPicker _cats = new CategoryPicker();
        readonly ComboBox _catMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly CheckedListBox _levels = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        readonly CheckBox _allLevels = new CheckBox { Text = "All levels", AutoSize = true, Checked = true };
        readonly RadioButton _rProject = new RadioButton { Text = "Whole project", AutoSize = true };
        readonly RadioButton _rSel = new RadioButton { AutoSize = true };
        readonly RadioButton _rView = new RadioButton { AutoSize = true };
        readonly CheckBox _doModel = new CheckBox { Text = "Model package (elements, families, geometry)", AutoSize = true, Checked = true };
        readonly CheckBox _doDocs = new CheckBox { Text = "Docs package (views, sheets, schedules, ...)", AutoSize = true, Checked = true };
        readonly CheckBox _saveDefaults = new CheckBox { Text = "Save these settings as my defaults", AutoSize = true };
        readonly TextBox _folder = new TextBox { Width = 480 };
        readonly TextBox _name = new TextBox { Width = 320 };
        public ExportStudioResult Result { get; private set; }

        public ExportStudioForm(ExportStudioInput input)
        {
            _in = input;
            _o = input.Options;
            Ui.StyleForm(this, "Downgrade export - " + input.ProjectTitle, 800, 680);

            BuildScopeTab();
            BuildOutputTab();
            BuildOptionPages();

            var export = Ui.Btn("Export", OnExport);
            var cancel = Ui.Btn("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 3, Padding = new Padding(8) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.Controls.Add(_saveDefaults, 0, 0);
            bar.Controls.Add(export, 1, 0);
            bar.Controls.Add(cancel, 2, 0);
            Controls.Add(_tabs);
            Controls.Add(bar);
            AcceptButton = export;
            CancelButton = cancel;
        }

        // ---------------------------------------------------------------- tabs
        void BuildScopeTab()
        {
            var page = new TabPage("What to export");
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // scope
            _rSel.Text = "Selected elements (" + _in.SelectedCount + ")";
            _rSel.Enabled = _in.SelectedCount > 0;
            _rView.Text = "Elements visible in the active view" + (string.IsNullOrEmpty(_in.ActiveViewName) ? "" : " (" + _in.ActiveViewName + ")");
            _rProject.Checked = _o.Scope == ExportScope.Project || (_o.Scope == ExportScope.Selection && _in.SelectedCount == 0);
            _rSel.Checked = _o.Scope == ExportScope.Selection && _in.SelectedCount > 0;
            _rView.Checked = _o.Scope == ExportScope.ActiveView;
            var gScope = new GroupBox { Text = "Scope", Dock = DockStyle.Fill, AutoSize = true };
            var fScope = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            fScope.Controls.AddRange(new Control[] { _rProject, _rSel, _rView });
            gScope.Controls.Add(fScope);
            t.Controls.Add(gScope, 0, 0);

            // categories
            _catMode.Items.AddRange(new object[] { "All categories", "Only the checked categories", "All except the checked categories" });
            _catMode.SelectedIndex = _o.IncludeCategories.Count > 0 ? 1 : (_o.ExcludeCategories.Count > 0 ? 2 : 0);
            List<string> seed = _o.IncludeCategories.Count > 0 ? _o.IncludeCategories : _o.ExcludeCategories;
            _cats.SetItems(seed.Select(k => new CatItem { Key = k, Name = k, Count = 0 }));
            _cats.SetChecked(seed);
            var scan = Ui.Btn("Scan model for categories", (s, e) =>
            {
                if (_in.ScanCategories == null) return;
                Cursor = Cursors.WaitCursor;
                try { _cats.SetItems(_in.ScanCategories()); }
                finally { Cursor = Cursors.Default; }
            });
            var gCats = new GroupBox { Text = "Categories", Dock = DockStyle.Fill };
            var tc = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            tc.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tc.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tc.Controls.Add(Ui.Line(_catMode, scan), 0, 0);
            tc.Controls.Add(_cats, 0, 1);
            gCats.Controls.Add(tc);
            t.Controls.Add(gCats, 0, 1);

            // levels
            var gLev = new GroupBox { Text = "Levels", Dock = DockStyle.Fill };
            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (string l in _in.Levels) _levels.Items.Add(l, _o.LevelNames.Contains(l));
            _allLevels.Checked = _o.LevelNames.Count == 0;
            _levels.Enabled = !_allLevels.Checked;
            _allLevels.CheckedChanged += (s, e) => _levels.Enabled = !_allLevels.Checked;
            tl.Controls.Add(_allLevels, 0, 0);
            tl.Controls.Add(_levels, 0, 1);
            gLev.Controls.Add(tl);
            t.Controls.Add(gLev, 0, 2);

            // packages
            t.Controls.Add(Ui.Line(_doModel, _doDocs), 0, 3);
            page.Controls.Add(t);
            _tabs.TabPages.Add(page);
        }

        void BuildOutputTab()
        {
            var page = new TabPage("Output");
            var p = Ui.Page();
            string title = _in.ProjectTitle ?? "project";
            if (title.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) title = title.Substring(0, title.Length - 4);
            _folder.Text = _in.DefaultFolder ?? "";
            _name.Text = Ui.SafeName(title);
            var browse = Ui.Btn("Browse...", (s, e) =>
            {
                string d = PackageFolder.PickFolder("Folder that will contain the .downgrade package", _folder.Text);
                if (d != null) _folder.Text = d;
            });
            p.Controls.Add(Ui.Lbl("Output folder:"));
            p.Controls.Add(Ui.Line(_folder, browse));
            p.Controls.Add(Ui.Lbl("Package name (a '.downgrade' folder with this name is created):"));
            p.Controls.Add(_name);
            p.Controls.Add(Ui.Hint("Pick an existing *.downgrade folder as output to add to / refresh that package (for example the docs package after a model package)."));
            p.Controls.Add(Ui.Check("Show the report window when finished", _o.OpenReportWhenDone, v => _o.OpenReportWhenDone = v));
            page.Controls.Add(p);
            _tabs.TabPages.Add(page);
        }

        void BuildOptionPages()
        {
            int sel = _tabs.SelectedIndex;
            foreach (TabPage p in _optPages) _tabs.TabPages.Remove(p);
            _optPages.Clear();
            int at = 1;
            foreach (TabPage p in OptionsPages.Create(_o, false, _tt, BuildOptionPages)) { _tabs.TabPages.Insert(at++, p); _optPages.Add(p); }
            if (sel >= 0 && sel < _tabs.TabPages.Count) _tabs.SelectedIndex = sel;
        }

        // ---------------------------------------------------------------- run
        void OnExport(object sender, EventArgs e)
        {
            if (!_doModel.Checked && !_doDocs.Checked) { MessageBox.Show(this, "Tick at least one package.", "Downgrade export"); return; }
            if (string.IsNullOrWhiteSpace(_folder.Text)) { MessageBox.Show(this, "Choose an output folder.", "Downgrade export"); _tabs.SelectedIndex = _tabs.TabPages.Count - 1; return; }

            _o.Scope = _rSel.Checked ? ExportScope.Selection : (_rView.Checked ? ExportScope.ActiveView : ExportScope.Project);
            List<string> keys = _cats.CheckedKeys;
            _o.IncludeCategories = _catMode.SelectedIndex == 1 ? keys : new List<string>();
            _o.ExcludeCategories = _catMode.SelectedIndex == 2 ? keys : new List<string>();
            _o.LevelNames = _allLevels.Checked ? new List<string>() : _levels.CheckedItems.Cast<string>().ToList();
            if (_catMode.SelectedIndex != 0 && keys.Count == 0)
            {
                MessageBox.Show(this, "The category filter is active but no category is checked.", "Downgrade export");
                _tabs.SelectedIndex = 0;
                return;
            }

            string folder = _folder.Text.Trim();
            string root = folder.EndsWith(".downgrade", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(folder, "manifest.json"))
                ? folder : Path.Combine(folder, Ui.SafeName(_name.Text) + ".downgrade");
            Result = new ExportStudioResult { PackageRoot = root, Model = _doModel.Checked, Docs = _doDocs.Checked, SaveAsDefaults = _saveDefaults.Checked, Options = _o };
            DialogResult = DialogResult.OK;
        }
    }
}
