using Autodesk.Revit.DB;
using SharpGLTF.Materials;

namespace RevitTrueGltf.ExportStrategies
{
    /// <summary>
    /// Converts a Revit MaterialNode into a glTF MaterialBuilder.
    /// 
    /// A single strategy instance is created per export session in ExportGltfContext.Start()
    /// and reused for every OnMaterial() call. Results are cached by the context via
    /// _materialBuilderCache, so Build() is invoked at most once per unique material.
    /// </summary>
    public interface IMaterialStrategy
    {
        MaterialBuilder Build(MaterialNode node);
    }
}
