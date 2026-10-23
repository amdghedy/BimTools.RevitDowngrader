using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Downgrader.Core.Storage;
using Downgrader.Core.Util;

namespace Downgrader.RevitShared
{
    internal class ImportStudioInput
    {
        public string InitialPackage;
        public List<string> Recent = new List<string>();
    }

    internal class ImportStudioResult
    {
        public string PackageRoot;
        public bool Model;
        public bool Docs;
        public DowngradeOptions Options;
    }

    /// <summary>Pick a package, see what is inside, choose what to import and how, then import.</summary>
    internal class ImportStudioForm : Form
    {
        readonly ToolTip _tt = new ToolTip();
        readonly ComboBox _package = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 560 };
        readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };
        readonly TextBox _info = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
        readonly CategoryPicker _cats = new CategoryPicker();
        readonly ComboBox _catMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly CheckedListBox _levels = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        readonly CheckBox _allLevels = new CheckBox { Text = "All levels", AutoSize = true, Checked = true };
        readonly CheckBox _doModel = new CheckBox { Text = "Model package", AutoSize = true };
        readonly CheckBox _doDocs = new CheckBox { Text = "Docs package", AutoSize = true };
        readonly CheckBox _dry = new CheckBox { Text = "Dry run: do everything, write the report, then roll back (nothing is kept)", AutoSize = true };
        readonly CheckBox _showReport = new CheckBox { Text = "Show the report window when finished", AutoSize = true };
        readonly Button _import;
        readonly List<TabPage> _optPages = new List<TabPage>();
        PackagePaths _paths;
        DowngradeOptions _o;
        PackageManifest _manifest;
        public ImportStudioResult Result { get; private set; }

        public ImportStudioForm(ImportStudioInput input)
        {
            Ui.StyleForm(this, "Downgrade import", 840, 700);

            _catMode.Items.AddRange(new object[] { "All categories", "Only the checked categories", "All except the checked categories" });
            _catMode.SelectedIndex = 0;
            foreach (string r in input.Recent) _package.Items.Add(r);

            var browse = Ui.Btn("Browse...", (s, e) =>
            {
                string d = PackageFolder.PickForImport();
                if (d != null) { _package.Text = d; LoadPackage(d); }
            });
            var load = Ui.Btn("Load", (s, e) => LoadPackage(_package.Text.Trim()));
            _package.SelectedIndexChanged += (s, e) => LoadPackage(_package.Text.Trim());
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 0) };
            top.Controls.Add(Ui.Lbl("Package:"));
            top.Controls.Add(_package);
            top.Controls.Add(browse);
            top.Controls.Add(load);

            var pInfo = new TabPage("Package");
            pInfo.Controls.Add(_info);
            _tabs.TabPages.Add(pInfo);
            BuildWhatTab();

            _import = Ui.Btn("Import", OnImport);
            _import.Enabled = false;
            var cancel = Ui.Btn("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
            bar.Controls.Add(cancel);
            bar.Controls.Add(_import);

            Controls.Add(_tabs);
            Controls.Add(bar);
            Controls.Add(top);
            AcceptButton = _import;
            CancelButton = cancel;

            if (!string.IsNullOrEmpty(input.InitialPackage)) { _package.Text = input.InitialPackage; LoadPackage(input.InitialPackage); }
            else _info.Text = "Choose a *.downgrade package folder (the one containing manifest.json).";
        }

        void BuildWhatTab()
        {
            var page = new TabPage("What to import");
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(Ui.Line(_doModel, _doDocs), 0, 0);

            var gCats = new GroupBox { Text = "Categories (model package)", Dock = DockStyle.Fill };
            var tc = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            tc.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tc.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tc.Controls.Add(_catMode, 0, 0);
            tc.Controls.Add(_cats, 0, 1);
            gCats.Controls.Add(tc);
            t.Controls.Add(gCats, 0, 1);

            var gLev = new GroupBox { Text = "Levels", Dock = DockStyle.Fill };
            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _allLevels.CheckedChanged += (s, e) => _levels.Enabled = !_allLevels.Checked;
            tl.Controls.Add(_allLevels, 0, 0);
            tl.Controls.Add(_levels, 0, 1);
            gLev.Controls.Add(tl);
            t.Controls.Add(gLev, 0, 2);

            var run = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            run.Controls.Add(_dry);
            run.Controls.Add(_showReport);
            t.Controls.Add(run, 0, 3);
            page.Controls.Add(t);
            _tabs.TabPages.Add(page);
        }

        void BuildOptionPages()
        {
            int sel = _tabs.SelectedIndex;
            foreach (TabPage p in _optPages) _tabs.TabPages.Remove(p);
            _optPages.Clear();
            if (_o == null) return;
            int at = 2;
            foreach (TabPage p in OptionsPages.Create(_o, true, _tt, BuildOptionPages)) { _tabs.TabPages.Insert(at++, p); _optPages.Add(p); }
            if (sel >= 0 && sel < _tabs.TabPages.Count) _tabs.SelectedIndex = sel;
        }

        void LoadPackage(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(Path.Combine(path, "manifest.json")))
            {
                _import.Enabled = false;
                _info.Text = "manifest.json not found in:\r\n" + path;
                return;
            }
            Cursor = Cursors.WaitCursor;
            try
            {
                _paths = new PackagePaths(path);
                _o = DowngradeOptions.LoadOrCreate(_paths);
                PackageSummary s = PackageInfo.Load(_paths);
                _manifest = s.Manifest;
                _info.Text = s.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                _doModel.Enabled = _manifest.HasModel; _doModel.Checked = _manifest.HasModel;
                _doDocs.Enabled = _manifest.HasDocs; _doDocs.Checked = _manifest.HasDocs;

                List<string> seed = _o.IncludeCategories.Count > 0 ? _o.IncludeCategories : _o.ExcludeCategories;
                _catMode.SelectedIndex = _o.IncludeCategories.Count > 0 ? 1 : (_o.ExcludeCategories.Count > 0 ? 2 : 0);
                _cats.SetItems(s.Categories);
                _cats.SetChecked(seed);
                _levels.Items.Clear();
                foreach (string l in s.Levels) _levels.Items.Add(l, _o.LevelNames.Contains(l));
                _allLevels.Checked = _o.LevelNames.Count == 0;
                _levels.Enabled = !_allLevels.Checked;
                _dry.Checked = _o.DryRun;
                _showReport.Checked = _o.OpenReportWhenDone;
                BuildOptionPages();
                _import.Enabled = true;
            }
            catch (Exception ex) { _import.Enabled = false; _info.Text = "Could not read the package:\r\n" + ex.Message; }
            finally { Cursor = Cursors.Default; }
        }

        void OnImport(object sender, EventArgs e)
        {
            if (_o == null || _paths == null) return;
            if (!_doModel.Checked && !_doDocs.Checked) { MessageBox.Show(this, "Tick at least one package.", "Downgrade import"); return; }
            List<string> keys = _cats.CheckedKeys;
            if (_catMode.SelectedIndex != 0 && keys.Count == 0) { MessageBox.Show(this, "The category filter is active but no category is checked.", "Downgrade import"); _tabs.SelectedIndex = 1; return; }
            _o.IncludeCategories = _catMode.SelectedIndex == 1 ? keys : new List<string>();
            _o.ExcludeCategories = _catMode.SelectedIndex == 2 ? keys : new List<string>();
            _o.LevelNames = _allLevels.Checked ? new List<string>() : _levels.CheckedItems.Cast<string>().ToList();
            _o.DryRun = _dry.Checked;
            _o.OpenReportWhenDone = _showReport.Checked;
            _o.Save(_paths);   // the importer reads its options from the package
            Result = new ImportStudioResult { PackageRoot = _paths.Root, Model = _doModel.Checked, Docs = _doDocs.Checked, Options = _o };
            DialogResult = DialogResult.OK;
        }
    }
}
