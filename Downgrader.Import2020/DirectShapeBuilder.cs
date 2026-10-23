using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Downgrader.Core.Schema;
using Downgrader.Core.Storage;
using Downgrader.RevitShared;

namespace Downgrader.Import
{
    /// <summary>The universal fallback: category + identity + triangle geometry with per-face materials.</summary>
    public static class DirectShapeBuilder
    {
        public static ElementId Create(Document doc, ElementRec rec, List<MeshRec> meshes, Func<string, ElementId> matId, out string note)
        {
            note = null;
            BuiltInCategory bic = BuiltInCategory.OST_GenericModel;
            BuiltInCategory parsed;
            if (RevitHelpers.TryEnum(rec.Category, out parsed) && DirectShape.IsValidCategoryId(new ElementId(parsed), doc)) bic = parsed;
            else note = "Category '" + rec.Category + "' unavailable for DirectShape in 2020, used Generic Models";

            var shapes = new List<GeometryObject>();
            foreach (MeshRec m in meshes) AddMesh(m, shapes, matId);
            if (shapes.Count == 0) { note = "Mesh could not be turned into Revit geometry"; return null; }

            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(bic));
            ds.ApplicationId = "BIMTOOLS.Downgrader";
            ds.ApplicationDataId = rec.Key;
            try { ds.Name = ((rec.FamilyName ?? "") + " : " + (rec.TypeName ?? "")).Trim(' ', ':'); } catch { }
            ds.SetShape(shapes);
            ParamConv.Apply(ds, rec.Params, null);
            return ds.Id;
        }

        static XYZ V(MeshRec m, int i) { return new XYZ(m.Vertices[i * 3], m.Vertices[i * 3 + 1], m.Vertices[i * 3 + 2]); }

        static void AddMesh(MeshRec m, List<GeometryObject> shapes, Func<string, ElementId> matId)
        {
            ElementId mid = matId(m.Material) ?? ElementId.InvalidElementId;
            foreach (bool solid in new[] { true, false })
            {
                var b = new TessellatedShapeBuilder();
                b.OpenConnectedFaceSet(solid);
                int added = 0;
                for (int i = 0; i + 2 < m.Indices.Length; i += 3)
                {
                    XYZ p0 = V(m, m.Indices[i]), p1 = V(m, m.Indices[i + 1]), p2 = V(m, m.Indices[i + 2]);
                    if ((p1 - p0).CrossProduct(p2 - p0).GetLength() < 1e-9) continue;
                    try { b.AddFace(new TessellatedFace(new List<XYZ> { p0, p1, p2 }, mid)); added++; } catch { }
                }
                b.CloseConnectedFaceSet();
                if (added == 0) return;
                try
                {
                    b.Target = TessellatedShapeBuilderTarget.AnyGeometry;
                    b.Fallback = TessellatedShapeBuilderFallback.Mesh;
                    b.Build();
                    foreach (GeometryObject go in b.GetBuildResult().GetGeometricalObjects()) shapes.Add(go);
                    return;
                }
                catch { if (!solid) return; }
            }
        }
    }
}
