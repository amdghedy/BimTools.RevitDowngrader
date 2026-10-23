using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Downgrader.Core.Storage;

namespace Downgrader.Export
{
    /// <summary>Turns any element's fine geometry into per-material triangle meshes (family instances included, in host coordinates).</summary>
    public static class GeometryMesher
    {
        struct VKey : IEquatable<VKey>
        {
            public long X, Y, Z;
            public bool Equals(VKey o) { return X == o.X && Y == o.Y && Z == o.Z; }
            public override bool Equals(object obj) { return obj is VKey && Equals((VKey)obj); }
            public override int GetHashCode() { unchecked { return (int)(X * 73856093 ^ Y * 19349663 ^ Z * 83492791); } }
        }

        class Builder
        {
            public string Material;
            readonly List<double> _v = new List<double>();
            readonly List<int> _i = new List<int>();
            readonly Dictionary<VKey, int> _map = new Dictionary<VKey, int>();
            public int Tri { get { return _i.Count / 3; } }

            int Vertex(XYZ p)
            {
                var k = new VKey { X = (long)Math.Round(p.X * 1e6), Y = (long)Math.Round(p.Y * 1e6), Z = (long)Math.Round(p.Z * 1e6) };
                int idx;
                if (_map.TryGetValue(k, out idx)) return idx;
                idx = _v.Count / 3;
                _v.Add(p.X); _v.Add(p.Y); _v.Add(p.Z);
                _map[k] = idx;
                return idx;
            }

            public void AddTri(XYZ a, XYZ b, XYZ c)
            {
                int ia = Vertex(a), ib = Vertex(b), ic = Vertex(c);
                if (ia == ib || ib == ic || ia == ic) return;
                _i.Add(ia); _i.Add(ib); _i.Add(ic);
            }

            public MeshRec ToRec() { return new MeshRec { Material = Material, Vertices = _v.ToArray(), Indices = _i.ToArray() }; }
        }

        public static List<MeshRec> Mesh(Element e, Document doc, double detail)
        {
            var opt = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };
            GeometryElement ge = e.get_Geometry(opt);
            var map = new Dictionary<string, Builder>();
            if (ge != null) Visit(ge, doc, detail, map);
            return map.Values.Where(b => b.Tri > 0).Select(b => b.ToRec()).ToList();
        }

        static void Visit(GeometryElement ge, Document doc, double detail, Dictionary<string, Builder> map)
        {
            foreach (GeometryObject go in ge)
            {
                Solid s = go as Solid;
                if (s != null) { AddSolid(s, doc, detail, map); continue; }
                GeometryInstance gi = go as GeometryInstance;
                if (gi != null) { Visit(gi.GetInstanceGeometry(), doc, detail, map); continue; }   // already transformed to host coordinates
                GeometryElement sub = go as GeometryElement;
                if (sub != null) Visit(sub, doc, detail, map);
            }
        }

        static void AddSolid(Solid s, Document doc, double detail, Dictionary<string, Builder> map)
        {
            if (s.Faces.Size == 0 || s.Volume <= 1e-9) return;
            foreach (Face f in s.Faces)
            {
                Mesh m = f.Triangulate(detail);
                if (m == null) continue;
                string mat = "";
                var me = doc.GetElement(f.MaterialElementId) as Material;
                if (me != null) mat = me.Name;
                Builder b;
                if (!map.TryGetValue(mat, out b)) { b = new Builder { Material = mat }; map[mat] = b; }
                IList<XYZ> vs = m.Vertices;
                for (int t = 0; t < m.NumTriangles; t++)
                {
                    MeshTriangle tri = m.get_Triangle(t);
                    b.AddTri(vs[(int)tri.get_Index(0)], vs[(int)tri.get_Index(1)], vs[(int)tri.get_Index(2)]);
                }
            }
        }
    }
}
