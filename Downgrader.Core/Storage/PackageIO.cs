using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
#if NET47
using System.Web.Script.Serialization;
#else
using System.Text.Json;
#endif
using Downgrader.Core.Schema;

namespace Downgrader.Core.Storage
{
    /// <summary>Folder layout of a *.downgrade package.</summary>
    public class PackagePaths
    {
        public string Root { get; }
        public PackagePaths(string root) { Root = root; }

        public string Manifest { get { return Path.Combine(Root, "manifest.json"); } }
        public string Options { get { return Path.Combine(Root, "downgrade.options.json"); } }
        public string IdMapFile { get { return Path.Combine(Root, "idmap.json"); } }
        public string ModelDir { get { return Path.Combine(Root, "model"); } }
        public string ModelJson { get { return Path.Combine(ModelDir, "model.json"); } }
        public string GeometryPack { get { return Path.Combine(ModelDir, "geometry.pack"); } }
        public string FamiliesDir { get { return Path.Combine(ModelDir, "families"); } }
        public string RebuiltDir { get { return Path.Combine(Root, "rebuilt_rfa_2020"); } }
        public string DocsDir { get { return Path.Combine(Root, "docs"); } }
        public string DocsJson { get { return Path.Combine(DocsDir, "docs.json"); } }
        public string SchedulesDir { get { return Path.Combine(DocsDir, "schedules"); } }
        public string ReportDir { get { return Path.Combine(Root, "report"); } }

        public void EnsureDirs()
        {
            foreach (string d in new[] { Root, ModelDir, FamiliesDir, DocsDir, SchedulesDir, ReportDir })
                Directory.CreateDirectory(d);
        }
    }

    /// <summary>JSON via System.Web.Extensions: ships with .NET Framework, so there is no Newtonsoft version clash with Revit's own copy.</summary>
    public static class Json
    {
#if NET47
        // Revit 2020 / 2022 (.NET Framework): ships with the framework, no Newtonsoft clash with Revit's own copy
        static JavaScriptSerializer Make()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000 };
        }

        public static void Write<T>(string path, T obj, bool pretty)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string s = Make().Serialize(obj);
            if (pretty) s = Pretty(s);
            File.WriteAllText(path, s, new UTF8Encoding(false));
        }

        public static T Read<T>(string path)
        {
            return Make().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
        }
#else
        // Revit 2025 (.NET 8): System.Text.Json writes the same PascalCase layout, so packages are interchangeable between both builds
        static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public static void Write<T>(string path, T obj, bool pretty)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var o = new JsonSerializerOptions(Opts) { WriteIndented = pretty };
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(obj, o));
        }

        public static T Read<T>(string path)
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Opts);
        }
#endif

        public static string Pretty(string json)
        {
            var sb = new StringBuilder(json.Length * 2);
            int indent = 0;
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) sb.Append(json[++i]);
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append(c); }
                else if (c == '[' || c == '{')
                {
                    int close = c == '[' ? NumberArrayEnd(json, i) : -1;
                    if (close > 0) { sb.Append(json, i, close - i + 1); i = close; continue; }
                    sb.Append(c);
                    if (i + 1 < json.Length && (json[i + 1] == ']' || json[i + 1] == '}')) sb.Append(json[++i]);
                    else { indent++; NewLine(sb, indent); }
                }
                else if (c == ']' || c == '}') { indent--; NewLine(sb, indent); sb.Append(c); }
                else if (c == ',') { sb.Append(c); NewLine(sb, indent); }
                else if (c == ':') sb.Append(": ");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // keeps [1.0,2.0,3.0] style arrays on one line
        static int NumberArrayEnd(string s, int start)
        {
            for (int i = start + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ']') return i;
                if (!(char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E' || c == ',')) return -1;
            }
            return -1;
        }

        static void NewLine(StringBuilder sb, int indent)
        {
            sb.Append('\n');
            sb.Append(' ', Math.Max(0, indent) * 2);
        }
    }

    public static class ManifestIO
    {
        public static PackageManifest LoadOrNew(PackagePaths p)
        {
            return File.Exists(p.Manifest) ? Json.Read<PackageManifest>(p.Manifest) : new PackageManifest();
        }
        public static void Save(PackagePaths p, PackageManifest m) { Json.Write(p.Manifest, m, true); }
    }

    /// <summary>Source UniqueId -> ElementId (int) in the 2020 project. Written by the model import, consumed by the docs import.</summary>
    public class IdMap
    {
        public Dictionary<string, int> Map { get; set; } = new Dictionary<string, int>();

        public static IdMap LoadOrNew(PackagePaths p)
        {
            return File.Exists(p.IdMapFile) ? Json.Read<IdMap>(p.IdMapFile) : new IdMap();
        }
        public void Save(PackagePaths p) { Json.Write(p.IdMapFile, this, false); }
    }
}
