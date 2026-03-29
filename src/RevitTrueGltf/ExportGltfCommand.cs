using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitTrueGltf.Utils;
using System;

namespace RevitTrueGltf
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual),
    Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    public class ExportGltfCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            View3D activeView = doc.ActiveView as View3D;
            if (activeView == null)
            {
                TaskDialog.Show("Error", "Please make sure your active view is a 3D View before exporting.");
                return Result.Failed;
            }

            MaterialUtils.Init(commandData.Application.Application);
            ExportGltfContext context = new ExportGltfContext(doc);

            using (CustomExporter exporter = new CustomExporter(doc, context))
            {
                exporter.IncludeGeometricObjects = false;
                exporter.ShouldStopOnError = false;

                try
                {
                    exporter.Export(doc.ActiveView);

                    Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
                    saveFileDialog.Filter = "glTF Binary (*.glb)|*.glb|glTF JSON (*.gltf)|*.gltf";
                    saveFileDialog.Title = "Save Exported glTF";
                    saveFileDialog.FileName = doc.Title;

                    if (saveFileDialog.ShowDialog() == true)
                    {
                        context.Save(saveFileDialog.FileName);
                        TaskDialog.Show("Success", "Export glTF/glb Success");
                    }
                    else
                    {
                        return Result.Cancelled;
                    }

                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Export Error", $"An error occurred during export:\n{ex.Message}");
                    message = ex.Message;
                    return Result.Failed;
                }
            }
            return Result.Succeeded;
        }
    }
}
