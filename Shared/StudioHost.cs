using System;
using System.Windows.Forms;
using Downgrader.Core.Util;

namespace Downgrader.RevitShared
{
    /// <summary>The only entry point the Revit commands use for UI, so command files never import System.Windows.Forms
    /// (which would clash with Revit's Application / View names).</summary>
    internal static class StudioHost
    {
        public static ExportStudioResult ShowExport(ExportStudioInput input)
        {
            using (var f = new ExportStudioForm(input)) return f.ShowDialog() == DialogResult.OK ? f.Result : null;
        }

        public static ImportStudioResult ShowImport(ImportStudioInput input)
        {
            using (var f = new ImportStudioForm(input)) return f.ShowDialog() == DialogResult.OK ? f.Result : null;
        }

        public static void ShowReport(ConversionReport report, string folder, string title)
        {
            using (var f = new ReportForm(report, folder, title)) f.ShowDialog();
        }

        /// <summary>Runs the work with a progress window; throws OperationCanceledException if the user pressed Cancel.</summary>
        public static void RunWithProgress(string title, Action work)
        {
            using (var f = new ProgressForm(title))
            {
                JobProgress.Current = new JobProgress { Reporter = f.UpdateProgress, IsCancelled = () => f.CancelRequested };
                f.Show();
                f.Refresh();
                try { work(); }
                finally { JobProgress.Current = new JobProgress(); f.Close(); }
            }
        }

        public static void Info(string title, string text)
        {
            MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
