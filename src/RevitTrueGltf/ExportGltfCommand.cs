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

            // Prepare default path: use Revit file's directory if saved, otherwise fallback to Desktop
            string defaultFileName = System.IO.Path.ChangeExtension(doc.Title, ".glb");
            string docDirectory = string.IsNullOrWhiteSpace(doc.PathName) 
                ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) 
                : System.IO.Path.GetDirectoryName(doc.PathName);
            string defaultPath = System.IO.Path.Combine(docDirectory, defaultFileName);

            // Show settings dialog
            var settings = new ExportSettings { ExportFilePath = defaultPath };
            var vm = new ExportSettingsVM(settings);
            var window = new MainWindow(vm);
            if (window.ShowDialog() != true)
            {
                return Result.Cancelled;
            }

            ExportGltfContext context = new ExportGltfContext(doc, settings);

            using (CustomExporter exporter = new CustomExporter(doc, context))
            {
                exporter.IncludeGeometricObjects = false;
                exporter.ShouldStopOnError = false;

                try
                {
                    exporter.Export(doc.ActiveView);

                    // Path is already pre-selected in the UI
                    string exportPath = settings.ExportFilePath;
                    if (string.IsNullOrEmpty(exportPath))
                    {
                        return Result.Failed;
                    }

                    new GltfWriter(settings).Write(context.ToScene(), exportPath);
                    TaskDialog.Show("Success", "Export glTF/glb Success\nPath: " + exportPath);

                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Export Error", $"An error occurred during export:\n{ex.Message}");
                    message = ex.Message;
                    return Result.Failed;
                }
            }
        }
    }
}
