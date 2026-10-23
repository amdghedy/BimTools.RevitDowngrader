using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Downgrader.Core.Util;

namespace Downgrader.RevitShared
{
    /// <summary>The option tabs shared by the export studio, the import studio and the stand-alone options dialog.</summary>
    internal static class OptionsPages
    {
        public static List<TabPage> Create(DowngradeOptions o, bool forImport, ToolTip tt, Action presetApplied)
        {
            var pages = new List<TabPage>();

            // ------------------------------------------------------------ Mode & fidelity
            var p1 = Ui.Page();
            p1.Controls.Add(Ui.Line(Ui.Lbl("Preset:"),
                Ui.Btn("Fast", (s, e) => { o.ApplyPreset("Fast"); presetApplied(); }),
                Ui.Btn("Balanced", (s, e) => { o.ApplyPreset("Balanced"); presetApplied(); }),
                Ui.Btn("Max fidelity", (s, e) => { o.ApplyPreset("Max fidelity"); presetApplied(); })));
            p1.Controls.Add(Ui.Hint("Fast = geometry only, no family rebuild.   Balanced = native where it verifies.   Max fidelity = finer meshes, stricter verification."));

            var rHybrid = new RadioButton { Text = "Hybrid - rebuild natively where it verifies, DirectShape otherwise", AutoSize = true, Checked = o.Mode == ExportMode.Hybrid };
            var rGeo = new RadioButton { Text = "Geometry only - every element becomes a DirectShape (fastest, most robust)", AutoSize = true, Checked = o.Mode == ExportMode.GeometryOnly };
            rHybrid.CheckedChanged += (s, e) => { if (rHybrid.Checked) o.Mode = ExportMode.Hybrid; };
            rGeo.CheckedChanged += (s, e) => { if (rGeo.Checked) o.Mode = ExportMode.GeometryOnly; };
            p1.Controls.Add(rHybrid);
            p1.Controls.Add(rGeo);

            p1.Controls.Add(Ui.Check("Rebuild families as real 2020 families", o.RebuildFamilies, v => o.RebuildFamilies = v, tt, "Parameters, formulas, types, reference planes and extrusions are recreated."));
            p1.Controls.Add(Ui.Check("Place instances of partly rebuilt families natively", o.AllowPartialFamilies, v => o.AllowPartialFamilies = v, tt, "Off: instances of families with dropped geometry become DirectShapes."));
            if (!forImport)
            {
                p1.Controls.Add(Ui.Check("Native rooms", o.NativeRooms, v => o.NativeRooms = v));
                p1.Controls.Add(Ui.Check("Native structural beams", o.NativeBeams, v => o.NativeBeams = v));
                p1.Controls.Add(Ui.Check("Native MEP curves (pipes, ducts, conduits, cable trays)", o.NativeMep, v => o.NativeMep = v));
            }
            p1.Controls.Add(Ui.Check("Verify every native element against the source (bounding box)", o.VerifyNative, v => o.VerifyNative = v));
            p1.Controls.Add(Ui.Check("Verify every rebuilt family type against the source geometry", o.VerifyFamilyTypes, v => o.VerifyFamilyTypes = v, tt, "Compares the bounds of each family type after the family is loaded."));
            p1.Controls.Add(Ui.Check("Freeze families that are not parametric-safe (geometry per type equals the source)", o.FreezePartialFamilies, v => o.FreezePartialFamilies = v, tt, "Sweeps, blends, revolves, nested families and families that fail verification keep exact geometry per type."));
            p1.Controls.Add(Ui.Line(Ui.Lbl("Family type tolerance (ft)"), Ui.Num(o.FamilyVerifyFeet, 0, 2, 0.01, 3, v => o.FamilyVerifyFeet = v)));
            if (!forImport)
            {
                p1.Controls.Add(Ui.Check("Native roofs (footprint roofs with slopes)", o.NativeRoofs, v => o.NativeRoofs = v));
                p1.Controls.Add(Ui.Check("Store family type geometry (needed for verification / freezing)", o.StoreTypeGeometry, v => o.StoreTypeGeometry = v));
            }
            if (!forImport) p1.Controls.Add(Ui.Line(Ui.Lbl("Mesh detail (0 coarse - 1 fine)"), Ui.Num(o.MeshDetail, 0, 1, 0.1, 2, v => o.MeshDetail = v)));
            p1.Controls.Add(Ui.Line(Ui.Lbl("Warn above (ft)"), Ui.Num(o.WarnDeviationFeet, 0, 5, 0.01, 3, v => o.WarnDeviationFeet = v),
                                    Ui.Lbl("Fall back to DirectShape above (ft)"), Ui.Num(o.FlattenDeviationFeet, 0, 20, 0.05, 3, v => o.FlattenDeviationFeet = v)));
            if (forImport)
            {
                var force = new TextBox { Width = 440, Text = string.Join(", ", o.ForceDirectShapeCategories) };
                force.TextChanged += (s, e) => o.ForceDirectShapeCategories = force.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                p1.Controls.Add(Ui.Lbl("Always import these categories as DirectShapes (comma separated, e.g. OST_Walls, OST_Floors):"));
                p1.Controls.Add(force);
            }
            var page1 = new TabPage("Mode & fidelity");
            page1.Controls.Add(p1);
            pages.Add(page1);

            // ------------------------------------------------------------ Documentation
            var p2 = Ui.Page();
            p2.Controls.Add(Ui.Hint("What the docs package carries (package B)."));
            p2.Controls.Add(Ui.Check("Project information", o.DocsProjectInfo, v => o.DocsProjectInfo = v));
            p2.Controls.Add(Ui.Check("Project / shared parameters", o.DocsProjectParams, v => o.DocsProjectParams = v));
            p2.Controls.Add(Ui.Check("Views", o.DocsViews, v => o.DocsViews = v));
            p2.Controls.Add(Ui.Check("   rebuild view templates from the first view that used them", o.RebuildViewTemplates, v => o.RebuildViewTemplates = v));
            p2.Controls.Add(Ui.Check("Schedules", o.DocsSchedules, v => o.DocsSchedules = v));
            p2.Controls.Add(Ui.Check("Sheets and viewports", o.DocsSheets, v => o.DocsSheets = v));
            p2.Controls.Add(Ui.Check("Revisions", o.DocsRevisions, v => o.DocsRevisions = v));
            p2.Controls.Add(Ui.Check("Annotations (text notes, detail lines, filled regions)", o.DocsAnnotations, v => o.DocsAnnotations = v));
            p2.Controls.Add(Ui.Check("Tags", o.DocsTags, v => o.DocsTags = v));
            p2.Controls.Add(Ui.Check("Groups", o.DocsGroups, v => o.DocsGroups = v));
            p2.Controls.Add(Ui.Check("Linked models", o.DocsLinks, v => o.DocsLinks = v));
            p2.Controls.Add(Ui.Check("Worksets (enable worksharing and re-create user worksets)", o.PreserveWorksets, v => o.PreserveWorksets = v));
            if (!forImport) p2.Controls.Add(Ui.Check("Also write an IFC2x3 file into the package (fallback / cross-check)", o.AlsoExportIfc, v => o.AlsoExportIfc = v));
            if (forImport)
            {
                var link = new TextBox { Width = 400, Text = o.LinkSearchFolder ?? "" };
                link.TextChanged += (s, e) => o.LinkSearchFolder = link.Text;
                var browse = Ui.Btn("Browse...", (s, e) =>
                {
                    string d = PackageFolder.PickFolder("Folder holding the DOWNGRADED linked files", link.Text);
                    if (d != null) link.Text = d;
                });
                p2.Controls.Add(Ui.Lbl("Folder with the downgraded linked files (links are re-linked by name):"));
                p2.Controls.Add(Ui.Line(link, browse));
            }
            var page2 = new TabPage("Documentation");
            page2.Controls.Add(p2);
            pages.Add(page2);

            // ------------------------------------------------------------ Advanced: every option
            var grid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = o, PropertySort = PropertySort.Categorized, ToolbarVisible = false, HelpVisible = true };
            var page3 = new TabPage("Advanced");
            page3.Controls.Add(grid);
            page3.Enter += (s, e) => grid.Refresh();
            pages.Add(page3);

            return pages;
        }
    }
}
