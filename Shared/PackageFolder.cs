using System.IO;
using System.Windows.Forms;

namespace Downgrader.RevitShared
{
    public static class PackageFolder
    {
        /// <summary>Export: pick a parent folder (a *.downgrade package folder or one containing manifest.json is reused as is).</summary>
        public static string PickForExport(string projectTitle)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choose where to create the downgrade package. Pick an existing *.downgrade folder to add to it.";
                dlg.ShowNewFolderButton = true;
                if (dlg.ShowDialog() != DialogResult.OK) return null;
                string sel = dlg.SelectedPath;
                if (File.Exists(Path.Combine(sel, "manifest.json")) || sel.EndsWith(".downgrade", System.StringComparison.OrdinalIgnoreCase))
                    return sel;
                string name = projectTitle;
                if (name != null && name.EndsWith(".rvt", System.StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                return Path.Combine(sel, RevitHelpers.SafeFileName(name) + ".downgrade");
            }
        }

        public static string PickFolder(string description, string initial = null)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = description;
                if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) dlg.SelectedPath = initial;
                return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        public static string PickFile(string filter, string initialFile)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = filter;
                if (!string.IsNullOrEmpty(initialFile) && File.Exists(initialFile))
                {
                    dlg.InitialDirectory = Path.GetDirectoryName(initialFile);
                    dlg.FileName = Path.GetFileName(initialFile);
                }
                return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
            }
        }

        /// <summary>Import: pick the *.downgrade package folder (must contain manifest.json).</summary>
        public static string PickForImport()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Choose the *.downgrade package folder (the one containing manifest.json).";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() != DialogResult.OK) return null;
                if (!File.Exists(Path.Combine(dlg.SelectedPath, "manifest.json")))
                {
                    MessageBox.Show("manifest.json not found in the selected folder.", "Downgrade", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                return dlg.SelectedPath;
            }
        }
    }
}
