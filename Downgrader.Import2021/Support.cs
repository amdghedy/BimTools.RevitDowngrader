using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Downgrader.Core.Schema;
using Downgrader.Core.Storage;

namespace Downgrader.Import
{
    /// <summary>Deletes warnings, resolves or removes failing elements, so a batch import never stops on a Revit dialog.</summary>
    public class FailureSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor fa)
        {
            foreach (FailureMessageAccessor f in fa.GetFailureMessages())
            {
                FailureSeverity sev = f.GetSeverity();
                if (sev == FailureSeverity.Warning) fa.DeleteWarning(f);
                else if (sev == FailureSeverity.Error)
                {
                    if (f.HasResolutions()) fa.ResolveFailure(f);
                    else
                    {
                        var ids = f.GetFailingElementIds();
                        if (ids.Count > 0) fa.DeleteElements(new List<ElementId>(ids));
                    }
                }
            }
            return FailureProcessingResult.Continue;
        }
    }

    public class FamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = true; return true; }
        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        { source = FamilySource.Family; overwriteParameterValues = true; return true; }
    }

    public static class Tx
    {
        public static Transaction Start(Document doc, string name)
        {
            var t = new Transaction(doc, name);
            t.Start();
            FailureHandlingOptions fo = t.GetFailureHandlingOptions();
            fo.SetFailuresPreprocessor(new FailureSwallower());
            t.SetFailureHandlingOptions(fo);
            return t;
        }
    }

    /// <summary>Temporary shared-parameter file so shared parameters keep their GUIDs (needed for schedules).</summary>
    public class SharedParamHelper : IDisposable
    {
        readonly Application _app;
        readonly string _prev;
        readonly DefinitionGroup _grp;

        public SharedParamHelper(Application app, string path)
        {
            _app = app;
            _prev = app.SharedParametersFilename;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path))
                File.WriteAllText(path, "# This is a Revit shared parameter file.\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\r\n");
            app.SharedParametersFilename = path;
            DefinitionFile df = app.OpenSharedParameterFile();
            _grp = df.Groups.get_Item("Downgrade") ?? df.Groups.Create("Downgrade");
        }

        public ExternalDefinition GetOrCreate(string name, Guid guid, ParameterType type)
        {
            Definition d = _grp.Definitions.get_Item(name);
            if (d != null) return d as ExternalDefinition;
            var opts = new ExternalDefinitionCreationOptions(name, type) { GUID = guid };
            return _grp.Definitions.Create(opts) as ExternalDefinition;
        }

        public void Dispose() { try { _app.SharedParametersFilename = _prev; } catch { } }
    }

    /// <summary>Resolves ParamRec references (levels via the id map, materials / phases / phase filters by name).</summary>
    public class Resolver
    {
        readonly Document _doc;
        readonly IdMap _map;
        Dictionary<string, ElementId> _mat, _phase, _pf;

        public Resolver(Document doc, IdMap map) { _doc = doc; _map = map; }

        public void Invalidate() { _mat = null; }

        static Dictionary<string, ElementId> ByName(Document doc, Type t)
        {
            var d = new Dictionary<string, ElementId>();
            foreach (Element e in new FilteredElementCollector(doc).OfClass(t))
                if (!d.ContainsKey(e.Name)) d[e.Name] = e.Id;
            return d;
        }

        public ElementId Material(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_mat == null) _mat = ByName(_doc, typeof(Material));
            ElementId id;
            return _mat.TryGetValue(name, out id) ? id : null;
        }

        public ElementId Level(string key)
        {
            int v;
            return key != null && _map.Map.TryGetValue(key, out v) ? new ElementId(v) : null;
        }

        public ElementId Resolve(ParamRec r)
        {
            ElementId id;
            switch (r.RefKind)
            {
                case "Level": return Level(r.RefKey);
                case "Material": return Material(r.RefName);
                case "Phase":
                    if (_phase == null) _phase = ByName(_doc, typeof(Phase));
                    return r.RefName != null && _phase.TryGetValue(r.RefName, out id) ? id : null;
                case "PhaseFilter":
                    if (_pf == null) _pf = ByName(_doc, typeof(PhaseFilter));
                    return r.RefName != null && _pf.TryGetValue(r.RefName, out id) ? id : null;
            }
            return null;
        }
    }
}
