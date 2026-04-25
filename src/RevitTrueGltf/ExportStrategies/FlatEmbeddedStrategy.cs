using RevitTrueGltf.Models;
using SharpGLTF.Schema2;
using SharpGLTF.Scenes;
using System.Text.Json.Nodes;

namespace RevitTrueGltf.ExportStrategies
{
    public class FlatEmbeddedStrategy : IParameterExportStrategy
    {
        public void Initialize(string exportDirectory, NodeBuilder rootNode)
        {
            // Flat mode attaches directly to elements, no initialization needed
        }

        public void OnElement(ElementDataDTO elementData, NodeBuilder elementNode)
        {
            if (elementData == null) return;

            var extras = new JsonObject();
            extras["ElementId"] = elementData.ElementId;
            extras["UniqueId"] = elementData.UniqueId;
            extras["Name"] = elementData.Name;
            extras["Category"] = elementData.CategoryName;
            extras["CategoryKey"] = elementData.CategoryKey;

            // Level elements are the reference datum themselves — LevelName/LevelId would be circular.
            if (elementData.CategoryKey != "OST_Levels")
            {
                extras["LevelName"] = elementData.LevelName;
                extras["LevelUniqueId"] = elementData.LevelUniqueId;
            }

            var parameters = new JsonArray();
            foreach (var param in elementData.Parameters)
            {
                var pObj = new JsonObject();
                pObj["Name"] = param.Name;
                pObj["Value"] = param.Value;
                pObj["GroupName"] = param.GroupName;
                pObj["GroupKey"] = param.GroupKey;
                pObj["UniqueKey"] = param.UniqueKey;
                pObj["Source"] = param.Source.ToString();
                pObj["DataType"] = param.DataType.ToString();
                pObj["UnitType"] = param.UnitType;
                parameters.Add(pObj);
            }
            extras["Parameters"] = parameters;

            elementNode.Extras = extras;
        }

        public void FinalizeExport()
        {
            // Nothing to finalize
        }
    }
}
