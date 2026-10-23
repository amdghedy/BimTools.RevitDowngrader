using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Downgrader.Core.Schema;
using Downgrader.Core.Storage;
using Downgrader.Core.Util;
using Downgrader.RevitShared;

namespace Downgrader.Export
{
    /// <summary>One export run on one open document. Used by the studio, the quick buttons and the batch command.</summary>
    static class ExportJob
    {
        static HashSet<long> ScopeIds(Document doc, UIDocument uidoc, DowngradeOptions o)
        {
            if (uidoc == null || o.Scope == ExportScope.Project) return null;
            if (o.Scope == ExportScope.Selection)
            {
                var sel = new HashSet<long>(uidoc.Selection.GetElementIds().Select(i => Compat.IdValue(i)));
                if (sel.Count == 0) throw new InvalidOperationException("Scope is 'Selection' but nothing is selected.");
                return sel;
            }
            return new HashSet<long>(new FilteredElementCollector(doc, doc.ActiveView.Id).WhereElementIsNotElementType().Select(e => Compat.IdValue(e.Id)));
        }

        static void ExportIfc(Document doc, PackagePaths paths, ConversionReport report)
        {
            try
            {
                string dir = Path.Combine(paths.Root, "ifc");
                Directory.CreateDirectory(dir);
                var io = new IFCExportOptions { FileVersion = IFCVersion.IFC2x3CV2 };
                using (var t = new Transaction(doc, "Downgrade IFC export"))
                {
                    t.Start();
                    try { doc.Export(dir, RevitHelpers.SafeFileName(doc.Title), io); }
                    finally { t.RollBack(); }
                }
                report.Add("export-model", Tiers.Native, "IFC", "IFC2x3 fallback", null, null);
            }
            catch (Exception ex) { report.Add("export-model", Tiers.Skipped, "IFC", "IFC2x3 fallback", null, ex.Message); }
        }

        public static List<CatItem> ScanCategories(Document doc)
        {
            var map = new Dictionary<string, CatItem>();
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                Category c = e.Category;
                if (c == null || c.CategoryType != CategoryType.Model) continue;
                string key = RevitHelpers.CategoryName(c);
                if (key.Length == 0) continue;
                CatItem it;
                if (!map.TryGetValue(key, out it)) { it = new CatItem { Key = key, Name = c.Name }; map[key] = it; }
                it.Count++;
            }
            return map.Values.OrderByDescending(i => i.Count).ToList();
        }

        public static ConversionReport Run(Document doc, UIDocument uidoc, string root, bool model, bool docs, DowngradeOptions chosen = null)
        {
            var paths = new PackagePaths(root);
            paths.EnsureDirs();
            DowngradeOptions opts = chosen ?? DowngradeOptions.LoadOrCreate(paths);
            if (chosen != null) chosen.Save(paths);
            var report = new ConversionReport();
            PackageManifest manifest = ManifestIO.LoadOrNew(paths);
            manifest.SourceRevitVersion = doc.Application.VersionNumber;
            manifest.SourceRevitBuild = doc.Application.VersionBuild;
            manifest.ProjectName = doc.Title;
            manifest.ExportedUtc = DateTime.UtcNow.ToString("o");
            try
            {
                if (model) { new ModelExporter(doc, paths, opts, report, ScopeIds(doc, uidoc, opts)).Run(manifest); manifest.HasModel = true; }
                if (docs) { new DocsExporter(doc, paths, opts, report).Run(manifest); manifest.HasDocs = true; }
                if (model && opts.AlsoExportIfc) ExportIfc(doc, paths, report);
            }
            finally
            {
                ManifestIO.Save(paths, manifest);
                report.Save(paths, "export" + (model ? "-model" : "") + (docs ? "-docs" : ""));
            }
            UserState st = UserState.Load();
            st.LastExportFolder = Path.GetDirectoryName(root.TrimEnd('\\', '/'));
            st.Save();
            return report;
        }

        /// <summary>Runs the export with a progress window, then shows the report window.</summary>
        public static Result Execute(UIDocument uidoc, string root, bool model, bool docs, DowngradeOptions chosen, ref string message)
        {
            Document doc = uidoc.Document;
            ConversionReport report = null;
            try
            {
                StudioHost.RunWithProgress("Exporting " + doc.Title, () => { report = Run(doc, uidoc, root, model, docs, chosen); });
            }
            catch (OperationCanceledException)
            {
                TaskDialog.Show("Downgrade export", "Export cancelled. The package folder may contain partial data:\n" + root);
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                try { Directory.CreateDirectory(Path.Combine(root, "report")); File.WriteAllText(Path.Combine(root, "report", "export-error.txt"), ex.ToString()); } catch { }
                return Result.Failed;
            }
            DowngradeOptions o = DowngradeOptions.LoadOrCreate(new PackagePaths(root));
            if (o.OpenReportWhenDone) StudioHost.ShowReport(report, Path.Combine(root, "report"), "Downgrade export - " + doc.Title);
            else TaskDialog.Show("Downgrade export", report.SummaryText() + "\nPackage folder:\n" + root);
            return Result.Succeeded;
        }

        public static Result Quick(UIDocument uidoc, bool model, bool docs, ref string message)
        {
            if (uidoc.Document.IsFamilyDocument) { TaskDialog.Show("Downgrade export", "Open a project (.rvt), not a family."); return Result.Cancelled; }
            string root = PackageFolder.PickForExport(uidoc.Document.Title);
            if (root == null) return Result.Cancelled;
            return Execute(uidoc, root, model, docs, null, ref message);
        }
    }

    /// <summary>The main button: scope, categories, levels, presets, options and output in one window.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ExportStudioCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            UIDocument uidoc = data.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            if (doc.IsFamilyDocument) { TaskDialog.Show("Downgrade export", "Open a project (.rvt), not a family."); return Result.Cancelled; }
            UserState st = UserState.Load();
            var input = new ExportStudioInput
            {
                ProjectTitle = doc.Title,
                DefaultFolder = st.LastExportFolder,
                Options = DowngradeOptions.LoadDefaults(),
                Levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).Select(l => l.Name).ToList(),
                SelectedCount = uidoc.Selection.GetElementIds().Count,
                ActiveViewName = doc.ActiveView != null ? doc.ActiveView.Name : "",
                ScanCategories = () => ExportJob.ScanCategories(doc)
            };
            ExportStudioResult res = StudioHost.ShowExport(input);
            if (res == null) return Result.Cancelled;
            if (res.SaveAsDefaults) DowngradeOptions.SaveDefaults(res.Options);
            return ExportJob.Execute(uidoc, res.PackageRoot, res.Model, res.Docs, res.Options, ref message);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ExportModelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        { return ExportJob.Quick(data.Application.ActiveUIDocument, true, false, ref message); }
    }

    [Transaction(TransactionMode.Manual)]
    public class ExportDocsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        { return ExportJob.Quick(data.Application.ActiveUIDocument, false, true, ref message); }
    }

    /// <summary>Quick export: both packages with your saved default options, only asks for the folder.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ExportBothCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        { return ExportJob.Quick(data.Application.ActiveUIDocument, true, true, ref message); }
    }

    [Transaction(TransactionMode.Manual)]
    public class ExportOptionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            DowngradeOptions o = DowngradeOptions.LoadDefaults();
            if (OptionsDialog.Show(o, "Downgrade options - defaults for new packages")) DowngradeOptions.SaveDefaults(o);
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    public class LastReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            string p = UserState.Load().LastReport;
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) { TaskDialog.Show("Downgrade", "There is no report yet. Run an export first."); return Result.Cancelled; }
            StudioHost.ShowReport(Json.Read<ConversionReport>(p), Path.GetDirectoryName(p), "Last report - " + Path.GetFileNameWithoutExtension(p));
            return Result.Succeeded;
        }
    }

    /// <summary>Opens every .rvt in a folder (detached when workshared), exports both packages, closes it. Shows progress, can be cancelled.</summary>
    [Transaction(TransactionMode.Manual)]
    public class BatchExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            Autodesk.Revit.ApplicationServices.Application app = data.Application.Application;
            UserState st = UserState.Load();
            string src = PackageFolder.PickFolder("Folder with the Revit .rvt files to downgrade", null);
            if (src == null) return Result.Cancelled;
            string outRoot = PackageFolder.PickFolder("Output folder for the .downgrade packages", st.LastExportFolder);
            if (outRoot == null) return Result.Cancelled;

            DowngradeOptions defaults = DowngradeOptions.LoadDefaults();
            List<string> files = Directory.GetFiles(src, "*.rvt", defaults.BatchSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => !Regex.IsMatch(f, @"\.\d{4}\.rvt$")).OrderBy(f => f).ToList();   // skip Revit backup files
            if (files.Count == 0) { TaskDialog.Show("Batch export", "No .rvt files found."); return Result.Cancelled; }
            if (TaskDialog.Show("Batch export", files.Count + " file(s) will be opened and exported.\nLarge models can take a while (you can cancel). Continue?", TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
                return Result.Cancelled;

            var log = new StringBuilder();
            int ok = 0, fail = 0;
            bool cancelled = false;
            try
            {
                StudioHost.RunWithProgress("Batch export", () =>
                {
                    int i = 0;
                    foreach (string f in files)
                    {
                        i++;
                        JobProgress.Current.Prefix = "File " + i + "/" + files.Count + " (" + Path.GetFileName(f) + "): ";
                        JobProgress.Current.Report("opening", 0, 0, true);
                        Document d = null;
                        try
                        {
                            var oo = new OpenOptions();
                            if (defaults.BatchOpenDetached) oo.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                            d = app.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(f), oo);
                            string root = Path.Combine(outRoot, RevitHelpers.SafeFileName(Path.GetFileNameWithoutExtension(f)) + ".downgrade");
                            ExportJob.Run(d, null, root, true, true);
                            log.AppendLine("OK    " + f); ok++;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { log.AppendLine("FAIL  " + f + "  :  " + ex.Message); fail++; }
                        finally { if (d != null) { try { d.Close(false); } catch { } } }
                    }
                });
            }
            catch (OperationCanceledException) { cancelled = true; log.AppendLine("CANCELLED by the user"); }
            st = UserState.Load();
            st.LastExportFolder = outRoot;
            st.Save();
            string logFile = Path.Combine(outRoot, "batch-export-log.txt");
            File.WriteAllText(logFile, log.ToString());
            TaskDialog.Show("Batch export", ok + " exported, " + fail + " failed" + (cancelled ? ", cancelled" : "") + ".\nLog: " + logFile);
            return Result.Succeeded;
        }
    }
}
