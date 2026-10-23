using System;
using Autodesk.Revit.DB;

namespace Downgrader.RevitShared
{
    /// <summary>Bounds of a family type's 3D geometry, measured the same way in the exporter (source) and the importer (rebuilt family),
    /// so the two numbers can be compared. The symbol must be active (FamilySymbol.Activate inside a transaction).</summary>
    public static class SymbolGeometry
    {
        public static bool Bounds(FamilySymbol s, out double[] min, out double[] max)
        {
            min = null; max = null;
            GeometryElement ge = s.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false });
            if (ge == null) return false;
            double[] lo = { double.MaxValue, double.MaxValue, double.MaxValue };
            double[] hi = { -double.MaxValue, -double.MaxValue, -double.MaxValue };
            bool any = false;
            Visit(ge, lo, hi, ref any);
            if (!any) return false;
            min = lo; max = hi;
            return true;
        }

        static void Visit(GeometryElement ge, double[] lo, double[] hi, ref bool any)
        {
            foreach (GeometryObject go in ge)
            {
                Solid s = go as Solid;
                if (s != null)
                {
                    if (s.Faces.Size == 0 || s.Volume <= 1e-9) continue;
                    foreach (Face f in s.Faces)
                    {
                        Mesh m = f.Triangulate(0.3);
                        if (m == null) continue;
                        foreach (XYZ p in m.Vertices)
                        {
                            if (p.X < lo[0]) lo[0] = p.X; if (p.Y < lo[1]) lo[1] = p.Y; if (p.Z < lo[2]) lo[2] = p.Z;
                            if (p.X > hi[0]) hi[0] = p.X; if (p.Y > hi[1]) hi[1] = p.Y; if (p.Z > hi[2]) hi[2] = p.Z;
                            any = true;
                        }
                    }
                    continue;
                }
                GeometryInstance gi = go as GeometryInstance;
                if (gi != null) { Visit(gi.GetInstanceGeometry(), lo, hi, ref any); continue; }
                GeometryElement sub = go as GeometryElement;
                if (sub != null) Visit(sub, lo, hi, ref any);
            }
        }
    }
}
