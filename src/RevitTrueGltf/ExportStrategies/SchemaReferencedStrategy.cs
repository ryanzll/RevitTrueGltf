using RevitTrueGltf.Models;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace RevitTrueGltf.ExportStrategies
{
    /// <summary>
    /// Stores the Property Schema at the root of the glTF, 
    /// while each element node only contains indexed property values for efficiency.
    /// </summary>
    public class SchemaReferencedStrategy : IParameterExportStrategy
    {
        private List<ParameterDTO> _schemaDefinitions = new List<ParameterDTO>();
        private Dictionary<string, int> _definitionToIndex = new Dictionary<string, int>();
        private NodeBuilder _rootNode;

        public void Initialize(string exportDirectory, NodeBuilder rootNode)
        {
            _rootNode = rootNode;
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

            var paramValues = new JsonObject();
            foreach (var param in elementData.Parameters)
            {
                // Create a unique key for the definition (everything except the value)
                string defKey = $"{param.UniqueKey}_{param.Name}_{param.GroupName}_{param.DataType}";

                if (!_definitionToIndex.TryGetValue(defKey, out int index))
                {
                    index = _schemaDefinitions.Count;
                    _schemaDefinitions.Add(new ParameterDTO
                    {
                        UniqueKey = param.UniqueKey,
                        Name = param.Name,
                        GroupName = param.GroupName,
                        GroupKey = param.GroupKey,
                        DataType = param.DataType,
                        Source = param.Source,
                        UnitType = param.UnitType
                    });
                    _definitionToIndex[defKey] = index;
                }

                paramValues[index.ToString()] = param.Value;
            }

            extras["IndexedParameters"] = paramValues;
            elementNode.Extras = extras;
        }

        public void FinalizeExport()
        {
            if (_schemaDefinitions.Count == 0) return;
            if (_rootNode == null) return;

            // Build global schema
            var globalSchema = new JsonObject();
            for (int i = 0; i < _schemaDefinitions.Count; i++)
            {
                var def = _schemaDefinitions[i];
                var node = new JsonObject();
                node["UniqueKey"] = def.UniqueKey;
                node["Name"] = def.Name;
                node["GroupName"] = def.GroupName;
                node["GroupKey"] = def.GroupKey;
                node["DataType"] = def.DataType.ToString();
                node["Source"] = def.Source.ToString();
                node["UnitType"] = def.UnitType;
                globalSchema[i.ToString()] = node;
            }

            if (_rootNode.Extras == null) _rootNode.Extras = new JsonObject();
            _rootNode.Extras["ParameterSchema"] = globalSchema;
        }
    }
}
