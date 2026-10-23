using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Downgrader.Core.Schema;

namespace Downgrader.RevitShared
{
    /// <summary>Curve &lt;-&gt; CurveRec. Lines, arcs and circles are exact; everything else becomes a polyline (flagged Lossy).</summary>
    public static class CurveConv
    {
        public const double MinLen = 0.0026;   // Revit short-curve tolerance (feet)

        public static CurveRec ToRec(Curve c)
        {
            var rec = new CurveRec();
            Line ln = c as Line;
            if (ln != null)
            {
                rec.Kind = "Line";
                rec.A = RevitHelpers.Pt(ln.GetEndPoint(0));
                rec.B = RevitHelpers.Pt(ln.GetEndPoint(1));
                return rec;
            }
            Arc arc = c as Arc;
            if (arc != null)
            {
                XYZ s = arc.GetEndPoint(0), e = arc.GetEndPoint(1);
                if (s.DistanceTo(e) < 1e-9)
                {
                    rec.Kind = "Circle";
                    rec.Center = RevitHelpers.Pt(arc.Center);
                    rec.Radius = arc.Radius;
                    rec.XAxis = RevitHelpers.Pt(arc.XDirection);
                    rec.YAxis = RevitHelpers.Pt(arc.YDirection);
                    return rec;
                }
                rec.Kind = "Arc";
                rec.A = RevitHelpers.Pt(s);
                rec.B = RevitHelpers.Pt(e);
                rec.M = RevitHelpers.Pt(arc.Evaluate(0.5, true));
                return rec;
            }
            rec.Kind = "Poly";
            rec.Lossy = true;
            rec.Pts = new List<double[]>();
            foreach (XYZ p in c.Tessellate()) rec.Pts.Add(RevitHelpers.Pt(p));
            return rec;
        }

        static XYZ P(double[] a, double? z)
        {
            return new XYZ(a[0], a[1], z.HasValue ? z.Value : a[2]);
        }

        /// <summary>Rebuilds curves. When flattenZ is given every point is projected to that elevation (used for floor profiles).</summary>
        public static List<Curve> ToCurves(CurveRec r, double? flattenZ = null)
        {
            var list = new List<Curve>();
            switch (r.Kind)
            {
                case "Line":
                    {
                        XYZ a = P(r.A, flattenZ), b = P(r.B, flattenZ);
                        if (a.DistanceTo(b) >= MinLen) list.Add(Line.CreateBound(a, b));
                        break;
                    }
                case "Arc":
                    {
                        XYZ a = P(r.A, flattenZ), b = P(r.B, flattenZ), m = P(r.M, flattenZ);
                        if (a.DistanceTo(b) >= MinLen) list.Add(Arc.Create(a, b, m));
                        break;
                    }
                case "Circle":
                    {
                        XYZ c = P(r.Center, flattenZ);
                        list.Add(Arc.Create(c, r.Radius, 0, 2 * Math.PI, RevitHelpers.ToXyz(r.XAxis), RevitHelpers.ToXyz(r.YAxis)));
                        break;
                    }
                case "Poly":
                    {
                        for (int i = 0; i + 1 < r.Pts.Count; i++)
                        {
                            XYZ a = P(r.Pts[i], flattenZ), b = P(r.Pts[i + 1], flattenZ);
                            if (a.DistanceTo(b) >= MinLen) list.Add(Line.CreateBound(a, b));
                        }
                        break;
                    }
            }
            return list;
        }
    }
}
