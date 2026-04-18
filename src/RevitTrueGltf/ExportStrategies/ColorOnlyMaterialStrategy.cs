using Autodesk.Revit.DB;
using SharpGLTF.Materials;
using System.Numerics;

namespace RevitTrueGltf.ExportStrategies
{
    /// <summary>
    /// Builds materials using only the node's raw render color and transparency value.
    /// No Revit appearance asset lookups, no texture loading, no PBR property extraction.
    /// 
    /// Yields the smallest and fastest glTF output — the preferred strategy for the
    /// Draft export preset where visual fidelity is less important than file size and speed.
    /// </summary>
    public class ColorOnlyMaterialStrategy : IMaterialStrategy
    {
        public MaterialBuilder Build(MaterialNode node)
        {
            var color = node.Color;
            var alpha = (float)(1.0 - node.Transparency);
            var builder = new MaterialBuilder(node.NodeName)
                .WithBaseColor(new Vector4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, alpha));
            if (node.Transparency > 0)
                builder.WithAlpha(AlphaMode.BLEND);
            return builder;
        }
    }
}
