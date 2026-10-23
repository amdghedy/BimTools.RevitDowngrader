using System;
using System.Reflection;
using Autodesk.Revit.UI;
using Downgrader.RevitShared;

namespace Downgrader.Import
{
    /// <summary>Runs inside Revit 2020. Adds the import tools to the BIM TOOLS tab (created if it does not exist).</summary>
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            RibbonPanel panel = BimToolsRibbon.GetOrCreatePanel(app, "Downgrade (import)");
            if (panel == null) return Result.Succeeded;   // never block Revit startup with an error message
            string asm = Assembly.GetExecutingAssembly().Location;

            BimToolsRibbon.AddButton(panel, new PushButtonData("DG_ImportStudio", "Import...", asm, "Downgrader.Import.ImportStudioCommand")
            { ToolTip = "Pick a package, see what is inside, choose categories / levels / options (dry run available), import with progress and a result report" });
            BimToolsRibbon.AddButton(panel, new PushButtonData("DG_QuickImport", "Quick\nImport", asm, "Downgrader.Import.ImportBothCommand")
            { ToolTip = "Model then docs with the options stored in the package; only asks for the package folder" });
            BimToolsRibbon.AddSeparator(panel);
            BimToolsRibbon.AddButton(panel, new PushButtonData("DG_BatchImport", "Batch\nImport", asm, "Downgrader.Import.BatchImportCommand")
            { ToolTip = "Turn every *.downgrade package in a folder into a saved Revit 2020 project, using a template" });
            BimToolsRibbon.AddButton(panel, new PushButtonData("DG_PackageInfo", "Package\nInfo", asm, "Downgrader.Import.PackageInfoCommand")
            { ToolTip = "What a package contains and what to expect, without touching the project" });
            BimToolsRibbon.AddButton(panel, new PushButtonData("DG_ImportOptions", "Options", asm, "Downgrader.Import.ImportOptionsCommand")
            { ToolTip = "Edit the options stored inside a package" });
            BimToolsRibbon.AddButton(panel, new PushButtonData("DG_ImportReport", "Last\nReport", asm, "Downgrader.Import.LastReportCommand")
            { ToolTip = "Reopen the report window of the last import" });
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app) { return Result.Succeeded; }
    }
}
