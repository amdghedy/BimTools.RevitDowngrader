using System.Collections.Generic;

namespace Downgrader.Core.Schema
{
    /// <summary>Version-neutral curve. All lengths are Revit internal units (feet).</summary>
    public class CurveRec
    {
        public string Kind { get; set; }          // Line | Arc | Circle | Poly
        public double[] A { get; set; }           // Line/Arc start
        public double[] B { get; set; }           // Line/Arc end
        public double[] M { get; set; }           // Arc mid point
        public double[] Center { get; set; }      // Circle
        public double Radius { get; set; }
        public double[] XAxis { get; set; }
        public double[] YAxis { get; set; }
        public List<double[]> Pts { get; set; }   // Poly (tessellated spline / ellipse, lossy)
        public bool Lossy { get; set; }
    }

    public class TransformRec
    {
        public double[] Origin { get; set; }
        public double[] BasisX { get; set; }
        public double[] BasisY { get; set; }
        public double[] BasisZ { get; set; }
    }

    public class BoxRec
    {
        public TransformRec Transform { get; set; }
        public double[] Min { get; set; }
        public double[] Max { get; set; }
    }

    /// <summary>One parameter value, readable by both API versions.</summary>
    public class ParamRec
    {
        public string Name { get; set; }
        public string BuiltIn { get; set; }       // BuiltInParameter name (resolved by name so newer enum members never break 2020)
        public string Storage { get; set; }       // Double | Integer | String | ElementId
        public string Spec { get; set; }          // ParameterType name
        public double D { get; set; }
        public int I { get; set; }
        public string S { get; set; }
        public string RefKind { get; set; }       // Special | Level | Material | Phase | PhaseFilter | Skip
        public string RefKey { get; set; }        // UniqueId (levels)
        public string RefName { get; set; }       // name (materials, phases...)
        public string Assoc { get; set; }         // family doc only: associated family parameter name
    }

    public class PackageManifest
    {
        public int FormatVersion { get; set; } = 1;
        public string SourceRevitVersion { get; set; }
        public string SourceRevitBuild { get; set; }
        public string ProjectName { get; set; }
        public string ExportedUtc { get; set; }
        public bool HasModel { get; set; }
        public bool HasDocs { get; set; }
        public Dictionary<string, int> Counts { get; set; } = new Dictionary<string, int>();
    }
}
