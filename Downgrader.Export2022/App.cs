using System;
using System.Reflection;
using Autodesk.Revit.UI;
using Downgrader.RevitShared;

namespace Downgrader.Export
{
    /// <summary>Runs inside Revit 2021 - 2027. Adds the export tools to the BIM TOOLS tab (created if it does not exist).</summary>
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            RibbonPanel panel = BimToolsRibbon.GetOrCreatePanel(app, "Downgrade (export)");
            if (panel == null) return Result.Succeeded;   // never block Revit startup with an error message
            string asm = Assembly.GetExecutingAssembly().Location;

            BimToolsRibbon.AddButton(panel, new PushButtonData("DG_ExportStudio", "Export...", asm, "Downgrader.Export.ExportStudioCommand")
            { ToolTip = "Choose scope, categories, levels, fidelity and output, then export the model and docs packages with a progress window and a result report" });
            BimToolsRibbon.AddButton(panel, new PushButtonData("DG_QuickExport", "Quick\nExport", asm, "Downgrader.Export.ExportBothCommand")
            { ToolTip = "Both packages with your saved default options; only asks for the output folder" });
            BimToolsRibbon.AddSeparator(panel);
            BimToolsRibbon.AddButton(panel, new PushButtonData("DG_BatchExport", "Batch\nExport", asm, "Downgrader.Export.BatchExportCommand")
            { ToolTip = "Export every .rvt in a folder (both packages), with progress and cancel" });
            BimToolsRibbon.AddButton(panel, new PushButtonData("DG_ExportOptions", "Options", asm, "Downgrader.Export.ExportOptionsCommand")
            { ToolTip = "Presets, fidelity, documentation switches and every advanced option (your defaults)" });
            BimToolsRibbon.AddButton(panel, new PushButtonData("DG_ExportReport", "Last\nReport", asm, "Downgrader.Export.LastReportCommand")
            { ToolTip = "Reopen the report window of the last export" });
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) { return Result.Succeeded; }
    }
}
