using RevitTrueGltf.Models;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace RevitTrueGltf.ExportStrategies
{
    public interface IParameterExportStrategy
    {
        /// <summary>
        /// Initialize before export starts (e.g. open database connection, receive RootNode)
        /// </summary>
        void Initialize(string exportFilePath, NodeBuilder rootNode);

        /// <summary>
        /// Per-element processing (invoked when element DTO and glTF Node are ready)
        /// </summary>
        void OnElement(ElementDataDTO elementData, NodeBuilder elementNode);

        /// <summary>
        /// All elements traversal finished, called by Context in Finish to complete data Commit.
        /// </summary>
        void FinalizeExport();
    }
}
