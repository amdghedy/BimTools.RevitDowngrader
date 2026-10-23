using System.Collections.Generic;

namespace Downgrader.Core.Schema
{
    /// <summary>Everything needed to rebuild one loadable family in a 2020 family document.</summary>
    public class FamilyDefRec
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string CurrentTypeName { get; set; }
        public string Fidelity { get; set; }              // Full | Partial
        public List<string> Dropped { get; set; } = new List<string>();
        public List<FamParamRec> Params { get; set; } = new List<FamParamRec>();
        public List<FamTypeRec> Types { get; set; } = new List<FamTypeRec>();
        public List<RefPlaneRec> RefPlanes { get; set; } = new List<RefPlaneRec>();
        public List<FamDimRec> Dims { get; set; } = new List<FamDimRec>();
        public List<ExtrusionRec> Extrusions { get; set; } = new List<ExtrusionRec>();
        public List<NestedRec> Nested { get; set; } = new List<NestedRec>();
        public List<ParamRec> FamilyParams { get; set; } = new List<ParamRec>();      // family settings: Always vertical, Cut with voids, Shared, Room calculation point, OmniClass ...
        public List<SymbolicRec> Symbolics { get; set; } = new List<SymbolicRec>();   // 2D symbolic lines (door swings, plan symbols)
    }

    public class SymbolicRec
    {
        public CurveRec Curve { get; set; }
        public double[] PlaneOrigin { get; set; }
        public double[] PlaneNormal { get; set; }
        public string LineStyle { get; set; }
    }

    public class FamParamRec
    {
        public string Name { get; set; }
        public string Group { get; set; }
        public string Spec { get; set; }
        public string Storage { get; set; }
        public bool IsInstance { get; set; }
        public bool IsReporting { get; set; }
        public bool IsBuiltIn { get; set; }
        public bool IsShared { get; set; }
        public string Guid { get; set; }
        public string Formula { get; set; }
    }

    public class FamTypeRec
    {
        public string Name { get; set; }
        public List<ParamRec> Values { get; set; } = new List<ParamRec>();   // ParamRec.Name = family parameter name
    }

    public class RefPlaneRec
    {
        public string Name { get; set; }
        public double[] BubbleEnd { get; set; }
        public double[] FreeEnd { get; set; }
        public double[] Normal { get; set; }
        public List<ParamRec> Params { get; set; } = new List<ParamRec>();   // Is Reference, Defines Origin, ...
    }

    public class FamDimRec
    {
        public string Label { get; set; }
        public List<string> Planes { get; set; } = new List<string>();
        public string ViewName { get; set; }
        public double[] A { get; set; }
        public double[] B { get; set; }
    }

    public class ExtrusionRec
    {
        public bool IsSolid { get; set; }
        public string SketchPlaneName { get; set; }
        public double[] PlaneOrigin { get; set; }
        public double[] PlaneNormal { get; set; }
        public double Start { get; set; }
        public double End { get; set; }
        public List<List<CurveRec>> Loops { get; set; } = new List<List<CurveRec>>();
        public List<List<string>> BoundPlanes { get; set; } = new List<List<string>>();   // parallel to Loops: ref plane each profile line is locked to
        public string Subcategory { get; set; }
        public string VisibilityType { get; set; }                                    // Model | ViewSpecific
        public Dictionary<string, bool> Visibility { get; set; } = new Dictionary<string, bool>();   // IsShownInCoarse / Medium / Fine / PlanRCPCut / FrontBack ...
        public List<ParamRec> Params { get; set; } = new List<ParamRec>();
    }

    public class NestedRec
    {
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public double[] Origin { get; set; }
    }
}
