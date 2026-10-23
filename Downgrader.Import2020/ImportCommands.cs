using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Downgrader.Core.Schema;
using Downgrader.Core.Storage;
using Downgrader.Core.Util;
using Downgrader.RevitShared;

namespace Downgrader.Import
{
    /// <summary>One import run into one open document. Used by the studio, the quick buttons and the batch command.</summary>
    static class ImportJob
    {
        public static ConversionReport Run(Application app, Document doc, string root, bool model, bool docs, string linkFolderFallback)
        {
            var paths = new PackagePaths(root);
            PackageManifest manifest = ManifestIO.LoadOrNew(paths);
            DowngradeOptions opts = DowngradeOptions.LoadOrCreate(paths);
            if (string.IsNullOrEmpty(opts.LinkSearchFolder)) opts.LinkSearchFolder = linkFolderFallback;
            var merged = new ConversionReport();

            if (model)
            {
                if (!manifest.HasModel) throw new InvalidOperationException("The package has no model data.");
                var report = new ConversionReport();
                try { RunGrouped(doc, "model", opts.DryRun, true, () => new ModelImporter(app, doc, paths, opts, report).Run()); }
                finally { report.Save(paths, "import-model"); }
                merged.Entries.AddRange(report.Entries);
            }
            if (docs && opts.DryRun && model)
                merged.Add("docs", Tiers.NativeWithLoss, "Dry run", "Docs skipped", null, "A dry run of the model leaves no elements for the docs import to attach to.");
            else if (docs)
            {
                if (!manifest.HasDocs) throw new InvalidOperationException("The package has no docs data.");
                var report = new ConversionReport();
                try { RunGrouped(doc, "docs", true, opts.DryRun, () => new DocsImporter(app, doc, paths, opts, report).Run()); }
                finally { report.Save(paths, "import-docs"); }
                merged.Entries.AddRange(report.Entries);
            }
            if (opts.DryRun) merged.Add("import", Tiers.NativeWithLoss, "Dry run", "DRY RUN", null, "Everything was rolled back, the project is unchanged.");

            UserState st = UserState.Load();
            st.LastPackage = root;
            st.AddRecent(root);
            st.Save();
            return merged;
        }

        /// <summary>The model import is one undo step (Assimilate). A dry run rolls the whole group back.</summary>
        static void RunGrouped(Document doc, string what, bool rollback, bool useGroup, Action work)
        {
            if (!useGroup) { work(); return; }
            using (var tg = new TransactionGroup(doc, "Downgrade import - " + what))
            {
                tg.Start();
                try { work(); }
                catch { tg.RollBack(); throw; }
                if (rollback) tg.RollBack(); else tg.Assimilate();
            }
        }

        /// <summary>Runs the import with a progress window, then shows the report window.</summary>
        public static Result Execute(UIApplication uiapp, string root, bool model, bool docs, ref string message)
        {
            Document doc = uiapp.ActiveUIDocument.Document;
            if (doc.IsFamilyDocument) { TaskDialog.Show("Downgrade import", "Open (or create) a project, not a family."); return Result.Cancelled; }
            ConversionReport report = null;
            try
            {
                StudioHost.RunWithProgress("Importing " + Path.GetFileName(root), () => { report = Run(uiapp.Application, doc, root, model, docs, null); });
            }
            catch (OperationCanceledException)
            {
                TaskDialog.Show("Downgrade import", "Import cancelled. The model import is rolled back; the docs import may be partial.");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                try { Directory.CreateDirectory(Path.Combine(root, "report")); File.WriteAllText(Path.Combine(root, "report", "import-error.txt"), ex.ToString()); } catch { }
                return Result.Failed;
            }
            DowngradeOptions o = DowngradeOptions.LoadOrCreate(new PackagePaths(root));
            string title = (o.DryRun ? "DRY RUN - " : "") + "Downgrade import - " + Path.GetFileName(root);
            if (o.OpenReportWhenDone) StudioHost.ShowReport(report, Path.Combine(root, "report"), title);
            else TaskDialog.Show(title, report.SummaryText() + "\nReports: " + Path.Combine(root, "report"));
            return Result.Succeeded;
        }

        public static Result Quick(UIApplication uiapp, bool model, bool docs, ref string message)
        {
            string root = PackageFolder.PickForImport();
            if (root == null) return Result.Cancelled;
            return Execute(uiapp, root, model, docs, ref message);
        }
    }

    /// <summary>The main button: pick a package, see what is in it, choose what and how to import.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ImportStudioCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            UserState st = UserState.Load();
            var input = new ImportStudioInput { InitialPackage = st.LastPackage, Recent = st.RecentPackages.Where(Directory.Exists).ToList() };
            ImportStudioResult res = StudioHost.ShowImport(input);
            if (res == null) return Result.Cancelled;
            return ImportJob.Execute(data.Application, res.PackageRoot, res.Model, res.Docs, ref message);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ImportModelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        { return ImportJob.Quick(data.Application, true, false, ref message); }
    }

    [Transaction(TransactionMode.Manual)]
    public class ImportDocsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        { return ImportJob.Quick(data.Application, false, true, ref message); }
    }

    /// <summary>Quick import: model then docs with the options stored in the package.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ImportBothCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        { return ImportJob.Quick(data.Application, true, true, ref message); }
    }

    [Transaction(TransactionMode.Manual)]
    public class ImportOptionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            string root = PackageFolder.PickForImport();
            if (root == null) return Result.Cancelled;
            var paths = new PackagePaths(root);
            DowngradeOptions o = DowngradeOptions.LoadOrCreate(paths);
            if (OptionsDialog.Show(o, "Package options - " + Path.GetFileName(root), true)) o.Save(paths);
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class LastReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            string p = UserState.Load().LastReport;
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) { TaskDialog.Show("Downgrade", "There is no report yet. Run an import first."); return Result.Cancelled; }
            StudioHost.ShowReport(Json.Read<ConversionReport>(p), Path.GetDirectoryName(p), "Last report - " + Path.GetFileNameWithoutExtension(p));
            return Result.Succeeded;
        }
    }

    /// <summary>Shows what a package contains and what the import will do with it, without touching the project.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class PackageInfoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            string root = PackageFolder.PickForImport();
            if (root == null) return Result.Cancelled;
            try
            {
                var paths = new PackagePaths(root);
                DowngradeOptions o = DowngradeOptions.LoadOrCreate(paths);
                PackageSummary s = PackageInfo.Load(paths);
                TaskDialog.Show("Downgrade package info", s.Text + "\nMode: " + o.Mode + "   Dry run: " + o.DryRun + "   Verify native: " + o.VerifyNative);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    /// <summary>For every *.downgrade package: new project from the template, model import, docs import, SaveAs, close. Progress + cancel.</summary>
    [Transaction(TransactionMode.Manual)]
    public class BatchImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            Application app = data.Application.Application;
            UserState st = UserState.Load();
            string pkgRoot = PackageFolder.PickFolder("Folder that contains the *.downgrade packages", Path.GetDirectoryName(st.LastPackage ?? ""));
            if (pkgRoot == null) return Result.Cancelled;
            string template = PackageFolder.PickFile("Revit project templates (*.rte)|*.rte", st.LastTemplate ?? app.DefaultProjectTemplate);
            if (template == null) return Result.Cancelled;
            string outDir = PackageFolder.PickFolder("Output folder for the Revit 2020 projects (also used to find downgraded links)", st.LastOutputFolder);
            if (outDir == null) return Result.Cancelled;

            List<string> packages = Directory.GetDirectories(pkgRoot, "*.downgrade", SearchOption.TopDirectoryOnly)
                .Where(d => File.Exists(Path.Combine(d, "manifest.json"))).OrderBy(d => d).ToList();
            if (File.Exists(Path.Combine(pkgRoot, "manifest.json"))) packages.Add(pkgRoot);
            if (packages.Count == 0) { TaskDialog.Show("Batch import", "No packages found."); return Result.Cancelled; }
            if (TaskDialog.Show("Batch import", packages.Count + " package(s) will be imported into new projects and saved to:\n" + outDir + "\n\nContinue?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes) return Result.Cancelled;

            st.LastTemplate = template;
            st.LastOutputFolder = outDir;
            st.Save();

            var log = new StringBuilder();
            int ok = 0, fail = 0;
            bool cancelled = false;
            try
            {
                StudioHost.RunWithProgress("Batch import", () =>
                {
                    int i = 0;
                    foreach (string pkg in packages)
                    {
                        i++;
                        string name = Path.GetFileName(pkg);
                        if (name.EndsWith(".downgrade", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - ".downgrade".Length);
                        JobProgress.Current.Prefix = "Package " + i + "/" + packages.Count + " (" + name + "): ";
                        JobProgress.Current.Report("creating project", 0, 0, true);
                        Document nd = null;
                        try
                        {
                            nd = app.NewProjectDocument(template);
                            ImportJob.Run(app, nd, pkg, true, true, outDir);
                            nd.SaveAs(Path.Combine(outDir, RevitHelpers.SafeFileName(name) + "_2020.rvt"), new SaveAsOptions { OverwriteExistingFile = true });
                            log.AppendLine("OK    " + pkg); ok++;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { log.AppendLine("FAIL  " + pkg + "  :  " + ex.Message); fail++; }
                        finally { if (nd != null) { try { nd.Close(false); } catch { } } }
                    }
                });
            }
            catch (OperationCanceledException) { cancelled = true; log.AppendLine("CANCELLED by the user"); }
            string logFile = Path.Combine(outDir, "batch-import-log.txt");
            File.WriteAllText(logFile, log.ToString());
            TaskDialog.Show("Batch import", ok + " imported, " + fail + " failed" + (cancelled ? ", cancelled" : "") + ".\nLog: " + logFile +
                "\n\nTip: linked models are re-linked only if their downgraded file already exists in the output folder; import the linked files first or run 'Import Docs' again afterwards.");
            return Result.Succeeded;
        }
    }
}
