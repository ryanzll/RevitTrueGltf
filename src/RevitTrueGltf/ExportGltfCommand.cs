using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitTrueGltf.Utils;
using System;
using System.IO;

namespace RevitTrueGltf
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual),
    Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    public class ExportGltfCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            
            try
            {
                LoggingHelper.Initialize();
                
                Document doc = commandData.Application.ActiveUIDocument.Document;
                View3D activeView = doc.ActiveView as View3D;
                if (activeView == null)
                {
                    TaskDialog.Show("Error", "Please make sure your active view is a 3D View before exporting.");
                    return Result.Failed;
                }

                MaterialUtils.Init(commandData.Application.Application);

                // Prepare default path
                string defaultFileName = Path.ChangeExtension(doc.Title, ".glb");
                string docDirectory = string.IsNullOrWhiteSpace(doc.PathName) 
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) 
                    : Path.GetDirectoryName(doc.PathName);
                string defaultPath = Path.Combine(docDirectory, defaultFileName);

                // Show settings dialog
                var settings = new ExportSettings { ExportFilePath = defaultPath };
                var vm = new ExportSettingsVM(settings);
                var window = new MainWindow(vm);
                if (window.ShowDialog() != true)
                {
                    return Result.Cancelled;
                }

                string exportPath = settings.ExportFilePath;
                if (string.IsNullOrEmpty(exportPath))
                {
                    TaskDialog.Show("Error", "Export path is empty.");
                    return Result.Failed;
                }

                ExportGltfContext context = new ExportGltfContext(doc, settings);

                using (CustomExporter exporter = new CustomExporter(doc, context))
                {
                    exporter.IncludeGeometricObjects = false;
                    exporter.ShouldStopOnError = false;

                    try
                    {
                        exporter.Export(doc.ActiveView);
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
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
            }
        }

        private static System.Reflection.Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string folderPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string assemblyName = new System.Reflection.AssemblyName(args.Name).Name + ".dll";
            string assemblyPath = Path.Combine(folderPath, assemblyName);

            if (File.Exists(assemblyPath))
            {
                return System.Reflection.Assembly.LoadFrom(assemblyPath);
            }

            return null;
        }
    }
}
