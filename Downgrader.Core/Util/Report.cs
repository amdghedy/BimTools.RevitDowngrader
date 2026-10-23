using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Downgrader.Core.Storage;

namespace Downgrader.Core.Util
{
    /// <summary>How well an item survived the downgrade.</summary>
    public static class Tiers
    {
        public const string Native = "Native";                 // rebuilt as an editable 2020 element, verified
        public const string NativeWithLoss = "NativeWithLoss"; // rebuilt natively but something was dropped / deviates
        public const string DirectShape = "DirectShape";       // geometry + category + identity kept, parametric behaviour lost
        public const string Skipped = "Skipped";               // could not be carried over; see reason
    }

    public class ReportEntry
    {
        public string Stage { get; set; }
        public string Tier { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }
        public string Key { get; set; }
        public string Reason { get; set; }
    }

    public class ConversionReport
    {
        public List<ReportEntry> Entries { get; set; } = new List<ReportEntry>();

        public void Add(string stage, string tier, string category, string name, string key, string reason)
        {
            Entries.Add(new ReportEntry { Stage = stage, Tier = tier, Category = category, Name = name, Key = key, Reason = reason });
        }

        public string SummaryText()
        {
            var sb = new StringBuilder();
            foreach (var g in Entries.GroupBy(e => e.Stage + "  /  " + e.Tier).OrderBy(g => g.Key))
                sb.AppendLine(g.Key + ":  " + g.Count());
            var why = Entries.Where(e => e.Tier != Tiers.Native && !string.IsNullOrEmpty(e.Reason))
                             .GroupBy(e => e.Reason).OrderByDescending(g => g.Count()).Take(8).ToList();
            if (why.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Most common reasons for loss / fallback:");
                foreach (var g in why) sb.AppendLine("  " + g.Count() + " x " + g.Key);
            }
            return sb.ToString();
        }

        static string Q(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }

        public void Save(PackagePaths p, string stem)
        {
            Directory.CreateDirectory(p.ReportDir);
            Json.Write(Path.Combine(p.ReportDir, stem + ".json"), this, true);
            var sb = new StringBuilder();
            sb.AppendLine(SummaryText());
            sb.AppendLine("---- everything that is not plain Native ----");
            foreach (ReportEntry e in Entries.Where(x => x.Tier != Tiers.Native))
                sb.AppendLine(e.Stage + " | " + e.Tier + " | " + e.Category + " | " + e.Name + " | " + e.Reason);
            File.WriteAllText(Path.Combine(p.ReportDir, stem + ".txt"), sb.ToString(), new UTF8Encoding(false));

            var cat = new StringBuilder("Stage,Category,Native,NativeWithLoss,DirectShape,Skipped,Total\r\n");
            foreach (var g in Entries.GroupBy(e => e.Stage + "|" + (e.Category ?? "")).OrderBy(g => g.Key))
            {
                string[] k = g.Key.Split('|');
                cat.Append(Q(k[0])).Append(',').Append(Q(k[1])).Append(',')
                   .Append(g.Count(e => e.Tier == Tiers.Native)).Append(',').Append(g.Count(e => e.Tier == Tiers.NativeWithLoss)).Append(',')
                   .Append(g.Count(e => e.Tier == Tiers.DirectShape)).Append(',').Append(g.Count(e => e.Tier == Tiers.Skipped)).Append(',')
                   .Append(g.Count()).Append("\r\n");
            }
            File.WriteAllText(Path.Combine(p.ReportDir, stem + "-by-category.csv"), cat.ToString(), new UTF8Encoding(true));

            var csv = new StringBuilder("Stage,Tier,Category,Name,Reason\r\n");
            foreach (ReportEntry e in Entries)
                csv.Append(Q(e.Stage)).Append(',').Append(Q(e.Tier)).Append(',').Append(Q(e.Category)).Append(',').Append(Q(e.Name)).Append(',').Append(Q(e.Reason)).Append("\r\n");
            File.WriteAllText(Path.Combine(p.ReportDir, stem + ".csv"), csv.ToString(), new UTF8Encoding(true));

            var h = new StringBuilder();
            h.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>Downgrade report</title><style>body{font:13px 'Segoe UI',Arial,sans-serif;margin:20px}h2{margin:0 0 8px}")
             .Append("table{border-collapse:collapse;width:100%}th,td{border:1px solid #ddd;padding:4px 8px;text-align:left;vertical-align:top}th{background:#f3f3f3}")
             .Append("tr.Native td:nth-child(2){background:#e6f4ea}tr.NativeWithLoss td:nth-child(2){background:#fff4d6}tr.DirectShape td:nth-child(2){background:#e8f0fe}tr.Skipped td:nth-child(2){background:#fde7e9}")
             .Append("pre{background:#f7f7f7;padding:8px}</style></head><body><h2>Downgrade report - ").Append(System.Net.WebUtility.HtmlEncode(stem)).Append("</h2><pre>")
             .Append(System.Net.WebUtility.HtmlEncode(SummaryText())).Append("</pre>")
             .Append("<input placeholder=\"filter...\" style=\"width:300px;margin:6px 0\" onkeyup=\"var v=this.value.toLowerCase();document.querySelectorAll('#t tr.r').forEach(function(r){r.style.display=r.textContent.toLowerCase().indexOf(v)<0?'none':''})\">")
             .Append("<table id=\"t\"><tr><th>Stage</th><th>Tier</th><th>Category</th><th>Name</th><th>Reason</th></tr>");
            foreach (ReportEntry e in Entries)
                h.Append("<tr class=\"r ").Append(e.Tier).Append("\"><td>").Append(System.Net.WebUtility.HtmlEncode(e.Stage ?? "")).Append("</td><td>").Append(e.Tier)
                 .Append("</td><td>").Append(System.Net.WebUtility.HtmlEncode(e.Category ?? "")).Append("</td><td>").Append(System.Net.WebUtility.HtmlEncode(e.Name ?? ""))
                 .Append("</td><td>").Append(System.Net.WebUtility.HtmlEncode(e.Reason ?? "")).Append("</td></tr>");
            h.Append("</table></body></html>");
            File.WriteAllText(Path.Combine(p.ReportDir, stem + ".html"), h.ToString(), new UTF8Encoding(false));

            UserState.RememberReport(Path.Combine(p.ReportDir, stem + ".json"));
        }
    }
}
