using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Downgrader.Core.Schema;

namespace Downgrader.RevitShared
{
    /// <summary>Compiled into BOTH add-ins (linked source), against the 2022 and the 2020 API respectively.</summary>
    public static class RevitHelpers
    {
        public static double[] Pt(XYZ p) { return new[] { p.X, p.Y, p.Z }; }
        public static XYZ ToXyz(double[] a) { return new XYZ(a[0], a[1], a[2]); }

        /// <summary>Parses an enum member by name. Members that do not exist in the running Revit version simply return false,
        /// which is how "features newer than 2020" degrade instead of crashing.</summary>
        public static bool TryEnum<T>(string name, out T value) where T : struct
        {
            value = default(T);
            if (string.IsNullOrEmpty(name)) return false;
            T v;
            if (!Enum.TryParse(name, false, out v)) return false;
            if (!Enum.IsDefined(typeof(T), v)) return false;
            value = v;
            return true;
        }

        public static string CategoryName(Category c)
        {
            if (c == null) return "";
            long id = Compat.IdValue(c.Id);
            if (id >= 0) return "";
            // BuiltInCategory is Int32-based up to Revit 2023 and Int64-based from 2024, so never pass a raw int/long to Enum.IsDefined
            object value = Enum.ToObject(typeof(BuiltInCategory), id);
            return Enum.IsDefined(typeof(BuiltInCategory), value) ? value.ToString() : "";
        }

        public static string CategoryName(Element e)
        {
            return e == null ? "" : CategoryName(e.Category);
        }

        public static string UniqueName(string name, ISet<string> used)
        {
            if (string.IsNullOrEmpty(name)) name = "Unnamed";
            string n = name;
            int i = 2;
            while (used.Contains(n)) n = name + " (" + (i++) + ")";
            used.Add(n);
            return n;
        }

        public static string SafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }

        public static Parameter Bip(Element e, string bipName)
        {
            BuiltInParameter b;
            if (!TryEnum(bipName, out b)) return null;
            try { return e.get_Parameter(b); } catch { return null; }
        }

        public static int GetInt(Element e, string bipName, int dflt)
        {
            Parameter p = Bip(e, bipName);
            return p != null && p.HasValue && p.StorageType == StorageType.Integer ? p.AsInteger() : dflt;
        }

        public static double GetDouble(Element e, string bipName, double dflt)
        {
            Parameter p = Bip(e, bipName);
            return p != null && p.HasValue && p.StorageType == StorageType.Double ? p.AsDouble() : dflt;
        }
    }

    public static class BoxConv
    {
        public static TransformRec ToRec(Transform t)
        {
            return new TransformRec
            {
                Origin = RevitHelpers.Pt(t.Origin),
                BasisX = RevitHelpers.Pt(t.BasisX),
                BasisY = RevitHelpers.Pt(t.BasisY),
                BasisZ = RevitHelpers.Pt(t.BasisZ)
            };
        }

        public static Transform FromRec(TransformRec r)
        {
            Transform t = Transform.Identity;
            t.Origin = RevitHelpers.ToXyz(r.Origin);
            t.BasisX = RevitHelpers.ToXyz(r.BasisX);
            t.BasisY = RevitHelpers.ToXyz(r.BasisY);
            t.BasisZ = RevitHelpers.ToXyz(r.BasisZ);
            return t;
        }

        public static BoxRec ToRec(BoundingBoxXYZ b)
        {
            if (b == null) return null;
            return new BoxRec { Transform = ToRec(b.Transform), Min = RevitHelpers.Pt(b.Min), Max = RevitHelpers.Pt(b.Max) };
        }

        public static BoundingBoxXYZ FromRec(BoxRec r)
        {
            if (r == null) return null;
            return new BoundingBoxXYZ
            {
                Transform = FromRec(r.Transform),
                Min = RevitHelpers.ToXyz(r.Min),
                Max = RevitHelpers.ToXyz(r.Max)
            };
        }
    }
}
