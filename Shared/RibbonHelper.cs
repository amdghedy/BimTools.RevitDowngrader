using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.UI;

namespace Downgrader.RevitShared
{
    /// <summary>Puts every panel inside the "BIM TOOLS" ribbon tab and never throws.
    /// Dependency-free (only Autodesk.Revit.UI): copy this file into any other BIM TOOLS add-in so all of them share one tab,
    /// no matter which add-in Revit loads first.</summary>
    public static class BimToolsRibbon
    {
        public const string TabName = "BIM TOOLS";

        public static RibbonPanel GetOrCreatePanel(UIControlledApplication app, string panelName)
        {
            // 1. make sure the tab exists. CreateRibbonTab throws when another add-in already created it - that is fine.
            try { app.CreateRibbonTab(TabName); }
            catch (Exception) { }

            // 2. reuse the panel if it is already in the tab, otherwise create it there
            try
            {
                foreach (RibbonPanel p in app.GetRibbonPanels(TabName))
                    if (p.Name == panelName) return p;
                return app.CreateRibbonPanel(TabName, panelName);
            }
            catch (Exception ex) { Log("Tab '" + TabName + "' not available, falling back to the Add-Ins tab", ex); }

            // 3. last resort: Revit's default Add-Ins tab, so the add-in still loads without an error message
            try
            {
                foreach (RibbonPanel p in app.GetRibbonPanels())
                    if (p.Name == panelName) return p;
                return app.CreateRibbonPanel(panelName);
            }
            catch (Exception ex) { Log("Could not create ribbon panel '" + panelName + "'", ex); return null; }
        }

        /// <summary>Adds the button unless a button with the same name is already on the panel.</summary>
        public static void AddButton(RibbonPanel panel, PushButtonData data)
        {
            try
            {
                if (panel.GetItems().Any(i => i.Name == data.Name)) return;
                panel.AddItem(data);
            }
            catch (Exception ex) { Log("Could not add ribbon button '" + data.Name + "'", ex); }
        }

        public static void AddSeparator(RibbonPanel panel)
        {
            try { if (panel.GetItems().Count == 0) return; panel.AddSeparator(); }
            catch (Exception ex) { Log("Could not add ribbon separator", ex); }
        }

        static void Log(string what, Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIMTOOLS", "Downgrader");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "ribbon-error.txt"), DateTime.Now.ToString("s") + "  " + what + ": " + ex.Message + Environment.NewLine);
            }
            catch { }
        }
    }
}
