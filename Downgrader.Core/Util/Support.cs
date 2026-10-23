using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Downgrader.Core.Schema;
using Downgrader.Core.Storage;

namespace Downgrader.Core.Util
{
    /// <summary>One selectable category (or similar) in a picker list.</summary>
    public class CatItem
    {
        public string Key { get; set; }       // BuiltInCategory name, e.g. OST_Walls
        public string Name { get; set; }      // display name, e.g. Walls
        public int Count { get; set; }
        public override string ToString() { return Name + "  (" + Count + ")    " + Key; }
    }

    /// <summary>Progress + cancellation channel between the dialogs and the long-running exporters / importers.</summary>
    public class JobProgress
    {
        public static JobProgress Current { get; set; } = new JobProgress();

        public Action<string, int, int> Reporter;   // stage, current, total (total <= 0 = unknown)
        public Func<bool> IsCancelled;
        public string Prefix = "";                  // e.g. "File 2/10: " during batch runs
        DateTime _last = DateTime.MinValue;

        public void Report(string stage, int current, int total, bool force = false)
        {
            if (Reporter != null)
            {
                DateTime now = DateTime.UtcNow;
                if (force || (now - _last).TotalMilliseconds >= 120) { _last = now; Reporter(Prefix + stage, current, total); }
            }
            CheckCancel();
        }

        public void CheckCancel()
        {
            if (IsCancelled != null && IsCancelled()) throw new OperationCanceledException("Cancelled by the user.");
        }
    }

    /// <summary>Remembers folders, recent packages and the last report between Revit sessions.</summary>
    public class UserState
    {
        public string LastExportFolder { get; set; }
        public string LastOutputFolder { get; set; }
        public string LastTemplate { get; set; }
        public string LastPackage { get; set; }
        public string LastReport { get; set; }
        public List<string> RecentPackages { get; set; } = new List<string>();

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIMTOOLS", "Downgrader", "state.json"); }
        }

        public static UserState Load()
        {
            try { if (File.Exists(FilePath)) return Json.Read<UserState>(FilePath); } catch { }
            return new UserState();
        }

        public void Save() { try { Json.Write(FilePath, this, true); } catch { } }

        public void AddRecent(string package)
        {
            RecentPackages.RemoveAll(x => string.Equals(x, package, StringComparison.OrdinalIgnoreCase));
            RecentPackages.Insert(0, package);
            if (RecentPackages.Count > 10) RecentPackages.RemoveRange(10, RecentPackages.Count - 10);
        }

        public static void RememberReport(string jsonPath)
        {
            UserState s = Load();
            s.LastReport = jsonPath;
            s.Save();
        }
    }

    public class PackageSummary
    {
        public PackageManifest Manifest;
        public string Text;
        public List<CatItem> Categories = new List<CatItem>();
        public List<string> Levels = new List<string>();
    }

    public static class PackageInfo
    {
        public static PackageSummary Load(PackagePaths paths)
        {
            var s = new PackageSummary { Manifest = ManifestIO.LoadOrNew(paths) };
            PackageManifest mf = s.Manifest;
            var sb = new StringBuilder();
            sb.AppendLine("Source:    Revit " + mf.SourceRevitVersion + "  (build " + mf.SourceRevitBuild + ")");
            sb.AppendLine("Project:   " + mf.ProjectName);
            sb.AppendLine("Exported:  " + mf.ExportedUtc + " (UTC)");
            sb.AppendLine("Contains:  " + (mf.HasModel ? "model package   " : "") + (mf.HasDocs ? "docs package" : ""));

            if (mf.HasModel && File.Exists(paths.ModelJson))
            {
                var m = Json.Read<ModelPackage>(paths.ModelJson);
                sb.AppendLine().AppendLine("MODEL   " + m.Elements.Count + " elements, " + m.Levels.Count + " levels, " + m.Grids.Count + " grids, " + m.Materials.Count + " materials");
                foreach (var g in m.Elements.GroupBy(e => e.Class).OrderByDescending(g => g.Count()))
                    sb.AppendLine("   " + g.Key + ": " + g.Count() + (g.Key == "Other" ? "   (become DirectShapes)" : "   (native candidates, " + g.Count(e => e.FlattenReason != null) + " already known to fall back)"));
                foreach (var g in m.Elements.Where(e => e.FlattenReason != null).GroupBy(e => e.FlattenReason).OrderByDescending(g => g.Count()).Take(6))
                    sb.AppendLine("   - " + g.Count() + " x " + g.Key);
                sb.AppendLine("   Families: " + m.Families.Count(f => f.Fidelity == "Full") + " full, " + m.Families.Count(f => f.Fidelity == "Partial") + " partial, " + m.Families.Count(f => f.Fidelity == "None") + " not extractable");
                if (File.Exists(paths.GeometryPack)) sb.AppendLine("   Geometry pack: " + (new FileInfo(paths.GeometryPack).Length / 1048576.0).ToString("0.0") + " MB");
                s.Categories = m.Elements.Where(e => !string.IsNullOrEmpty(e.Category))
                    .GroupBy(e => e.Category).Select(grp => new CatItem { Key = grp.Key, Name = grp.First().CategoryName ?? grp.Key, Count = grp.Count() })
                    .OrderByDescending(c => c.Count).ToList();
                s.Levels = m.Levels.OrderBy(l => l.Elevation).Select(l => l.Name).ToList();
            }
            if (mf.HasDocs && File.Exists(paths.DocsJson))
            {
                var d = Json.Read<DocsPackage>(paths.DocsJson);
                sb.AppendLine().AppendLine("DOCS    " + d.Views.Count + " views, " + d.Sheets.Count + " sheets, " + d.Schedules.Count + " schedules, " + d.Tags.Count + " tags, " + d.TextNotes.Count + " text notes");
                sb.AppendLine("   " + d.Revisions.Count + " revisions, " + d.Links.Count + " links, " + d.Groups.Count + " groups, " + d.Worksets.Count + " worksets, " + d.DesignOptions.Count + " design options, " + d.ProjectParams.Count + " project parameters");
            }
            s.Text = sb.ToString();
            return s;
        }
    }
}
