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
                return Result.Failed;
            }

            MaterialUtils.Init(commandData.Application.Application);
            ExportGltfContext context = new ExportGltfContext(doc);

            // 创建 CustomExporter
            using (CustomExporter exporter = new CustomExporter(doc, context))
            {
                exporter.IncludeGeometricObjects = false;
                exporter.ShouldStopOnError = false;

                try
                {
                    exporter.Export(doc.ActiveView);
                    context.Save(@"C:\Users\ryan\Desktop\revit\test.glb");
                    TaskDialog.Show("Success", "Export Gltf Success");
                    return Result.Succeeded;
                }
                catch (Exception ex)
                {
                    message = ex.Message;
                    return Result.Failed;
                }
            }
            return Result.Succeeded;
        }
    }
}
