using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace Downgrader.RevitShared
{
    /// <summary>The few API differences between Revit 2020 / 2022 and Revit 2024+ live here.
    /// REVIT2022_OR_NEWER is defined by Export2022 and Export2025, REVIT2024_OR_NEWER by Export2025 only; the 2020 importer defines neither.
    /// Whatever the exporter reads is normalised to the 2020 vocabulary (ParameterType / BuiltInParameterGroup names),
    /// so the 2020 importer never has to know which Revit version produced the package.</summary>
    public static class Compat
    {
        public static long IdValue(ElementId id)
        {
#if REVIT2024_OR_NEWER
            return id.Value;               // ElementId.IntegerValue no longer exists
#else
            return id.IntegerValue;
#endif
        }

        public static ElementId MakeId(long v)
        {
#if REVIT2024_OR_NEWER
            return new ElementId(v);       // ElementId(int) no longer exists
#else
            return new ElementId((int)v);
#endif
        }

        /// <summary>The element a tag points at. GetTaggedLocalElements exists from 2022; Revit 2021 only has TaggedLocalElementId.</summary>
        public static Element TaggedElement(IndependentTag tg, Document doc)
        {
#if REVIT2022_OR_NEWER
            return tg.GetTaggedLocalElements().FirstOrDefault();
#else
            return doc.GetElement(tg.TaggedLocalElementId);
#endif
        }

        public static bool IsSlabShapeEdited(Floor f)
        {
#if REVIT2024_OR_NEWER
            SlabShapeEditor e = f.GetSlabShapeEditor();     // the SlabShapeEditor property is obsolete from 2024
#else
            SlabShapeEditor e = f.SlabShapeEditor;
#endif
            return e != null && e.IsEnabled;
        }

        /// <summary>Parameter data type as a Revit 2020 ParameterType member name ("" when there is no 2020 equivalent).</summary>
        public static string SpecName(Definition d)
        {
#if REVIT2022_OR_NEWER
            try { return ForgeMap.ToParameterTypeName(d.GetDataType().TypeId); } catch { return ""; }   // Definition.ParameterType is obsolete from 2022 and gone in 2023
#else
            return d.ParameterType.ToString();
#endif
        }

        /// <summary>Parameter group as a Revit 2020 BuiltInParameterGroup member name ("" = importer default PG_DATA).</summary>
        public static string GroupName(Definition d)
        {
#if REVIT2022_OR_NEWER
            try { return ForgeMap.ToGroupName(d.GetGroupTypeId().TypeId); } catch { return ""; }
#else
            return d.ParameterGroup.ToString();
#endif
        }
    }

    /// <summary>ForgeTypeId strings (Revit 2022+) -> Revit 2020 enum member names. Pure string logic, no Revit API.</summary>
    public static class ForgeMap
    {
        static string Token(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return "";
            int colon = typeId.LastIndexOf(':');
            string t = colon >= 0 ? typeId.Substring(colon + 1) : typeId;
            int dash = t.IndexOf('-');
            if (dash >= 0) t = t.Substring(0, dash);
            t = t.ToLowerInvariant();
            if (t.StartsWith("spec.")) t = t.Substring(5);
            return t;
        }

        public static string ToParameterTypeName(string typeId)
        {
            string t = Token(typeId);
            switch (t)
            {
                case "length": return "Length";
                case "area": return "Area";
                case "volume": return "Volume";
                case "angle": return "Angle";
                case "number": return "Number";
                case "int64": case "integer": return "Integer";
                case "string": return "Text";
                case "string.multiline": return "MultilineText";
                case "string.url": return "URL";
                case "bool": return "YesNo";
                case "reference.material": return "Material";
                case "reference.image": return "Image";
                case "reference.loadclassification": return "LoadClassification";
                case "slope": return "Slope";
                case "currency": return "Currency";
                case "force": return "Force";
                case "linearforce": return "LinearForce";
                case "areaforce": return "AreaForce";
                case "moment": return "Moment";
                case "massdensity": return "MassDensity";
            }
            string all = (typeId ?? "").ToLowerInvariant();
            if (all.Contains("airflow")) return "HVACAirflow";
            if (all.Contains("illuminance")) return "ElectricalIlluminance";
            if (all.Contains("luminousflux")) return "ElectricalLuminousFlux";
            if (all.Contains("apparentpower")) return "ElectricalApparentPower";
            if (all.Contains("frequency")) return "ElectricalFrequency";
            if (all.Contains("current")) return "ElectricalCurrent";
            if (all.Contains("potential")) return "ElectricalPotential";
            if (all.Contains("pressure")) return "HVACPressure";
            if (all.Contains("velocity")) return "HVACVelocity";
            if (all.Contains("temperature")) return "HVACTemperature";
            return "";   // no 2020 equivalent: the importer skips such parameters and says so in the report
        }

        public static string ToGroupName(string typeId)
        {
            switch (Token(typeId))
            {
                case "data": return "PG_DATA";
                case "geometry": case "dimensions": return "PG_GEOMETRY";
                case "constraints": return "PG_CONSTRAINTS";
                case "text": return "PG_TEXT";
                case "identitydata": return "PG_IDENTITY_DATA";
                case "materials": return "PG_MATERIALS";
                case "graphics": return "PG_GRAPHICS";
                case "mechanical": return "PG_MECHANICAL";
                case "mechanicalairflow": return "PG_MECHANICAL_AIRFLOW";
                case "mechanicalloads": return "PG_MECHANICAL_LOADS";
                case "electrical": return "PG_ELECTRICAL";
                case "electricalloads": return "PG_ELECTRICAL_LOADS";
                case "electricallighting": return "PG_ELECTRICAL_LIGHTING";
                case "electricalcircuiting": return "PG_ELECTRICAL_CIRCUITING";
                case "plumbing": return "PG_PLUMBING";
                case "structural": return "PG_STRUCTURAL";
                case "structuralanalysis": return "PG_STRUCTURAL_ANALYSIS";
                case "phasing": return "PG_PHASING";
                case "visibility": return "PG_VISIBILITY";
                case "ifcparameter": return "PG_IFC";
                case "construction": return "PG_CONSTRUCTION";
                case "fireprotection": return "PG_FIRE_PROTECTION";
                case "greenbuilding": return "PG_GREEN_BUILDING";
                case "energyanalysis": return "PG_ENERGY_ANALYSIS";
                case "analysisresults": return "PG_ANALYSIS_RESULTS";
                case "analyticalmodel": return "PG_ANALYTICAL_MODEL";
                case "title": return "PG_TITLE";
            }
            return "";
        }
    }
}
