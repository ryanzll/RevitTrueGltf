using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace RevitTrueGltf
{
    public class ExportGltfCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            View3D activeView = doc.ActiveView as View3D;

            ExportGltfContext context = new ExportGltfContext();

            // 创建 CustomExporter
            using (CustomExporter exporter = new CustomExporter(doc, context))
            {
                exporter.IncludeGeometricObjects = false;
                exporter.ShouldStopOnError = false;

                try
                {
                    exporter.Export(activeView);
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
