using System.Collections.Generic;

namespace Downgrader.Core.Schema
{
    /// <summary>Package A: model.json (+ geometry.pack + families/*.family.json).</summary>
    public class ModelPackage
    {
        public List<MaterialRec> Materials { get; set; } = new List<MaterialRec>();
        public List<LevelRec> Levels { get; set; } = new List<LevelRec>();
        public List<GridRec> Grids { get; set; } = new List<GridRec>();
        public List<TypeRec> Types { get; set; } = new List<TypeRec>();
        public List<FamilyRec> Families { get; set; } = new List<FamilyRec>();
        public List<ElementRec> Elements { get; set; } = new List<ElementRec>();
    }

    public class MaterialRec
    {
        public string Name { get; set; }
        public int[] Rgb { get; set; }
        public int Transparency { get; set; }
        public int Shininess { get; set; }
        public int Smoothness { get; set; }
        public string MaterialClass { get; set; }
    }

    public class LevelRec { public string Key { get; set; } public string Name { get; set; } public double Elevation { get; set; } }
    public class GridRec { public string Key { get; set; } public string Name { get; set; } public CurveRec Curve { get; set; } }

    public class LayerRec
    {
        public string Material { get; set; }
        public double Thickness { get; set; }
        public string Function { get; set; }      // MaterialFunctionAssignment name
    }

    public class TypeRec
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string FamilyName { get; set; }
        public string Category { get; set; }
        public string Kind { get; set; }          // Wall | Floor | Family | Other
        public double Width { get; set; }
        public int ShellExterior { get; set; }       // compound structure: layers outside the core
        public int ShellInterior { get; set; }
        public int StructuralIndex { get; set; } = -1;
        public List<LayerRec> Layers { get; set; } = new List<LayerRec>();
        public List<ParamRec> Params { get; set; } = new List<ParamRec>();
    }

    public class FamilyRec
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string PlacementType { get; set; }
        public bool Editable { get; set; }
        public string DefFile { get; set; }       // families/<name>.family.json (null when not extractable)
        public string Fidelity { get; set; }      // Full | Partial | None
        public List<string> Dropped { get; set; } = new List<string>();
        public List<string> TypeNames { get; set; } = new List<string>();
        public List<TypeBoxRec> TypeGeometry { get; set; } = new List<TypeBoxRec>();   // per type: measured bounds + mesh, used to verify (and if needed freeze) the rebuilt family
    }

    public class TypeBoxRec
    {
        public string Name { get; set; }
        public double[] Min { get; set; }
        public double[] Max { get; set; }
        public long GeoOffset { get; set; } = -1;
    }

    public class LocRec
    {
        public string Kind { get; set; }          // Point | Curve
        public double[] Point { get; set; }
        public double Rotation { get; set; }
        public CurveRec Curve { get; set; }
    }

    public class ElementRec
    {
        public string Key { get; set; }           // UniqueId
        public long SourceId { get; set; }
        public string Class { get; set; }         // Wall | Floor | FamilyInstance | Other
        public string Category { get; set; }      // BuiltInCategory name
        public string CategoryName { get; set; }
        public string TypeKey { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string LevelKey { get; set; }
        public string HostKey { get; set; }
        public string SystemName { get; set; }    // MEP curves: piping / duct system type name
        public string FlattenReason { get; set; } // non-null => exporter already knows it cannot be rebuilt natively
        public LocRec Loc { get; set; }
        public List<List<CurveRec>> Loops { get; set; }   // floors / roofs: boundary loops
        public List<List<bool>> LoopDefinesSlope { get; set; }   // roofs: footprint line defines slope
        public List<List<double>> LoopSlope { get; set; }        // roofs: slope angle of that line
        public List<ParamRec> Params { get; set; } = new List<ParamRec>();
        public bool Flipped { get; set; }
        public bool FacingFlipped { get; set; }
        public bool HandFlipped { get; set; }
        public long GeoOffset { get; set; } = -1;          // offset in geometry.pack, -1 = none
        public int GeoMeshes { get; set; }
        public double[] BBoxMin { get; set; }              // element bounding box, used to verify native rebuilds
        public double[] BBoxMax { get; set; }
    }
}
