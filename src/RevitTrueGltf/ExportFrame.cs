using Autodesk.Revit.DB;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using System.Numerics;

namespace RevitTrueGltf
{
    using MeshBuilderType = MeshBuilder<VertexPositionNormalTangent, VertexTexture1, VertexEmpty>;

    internal enum ExportFrameType
    {
        Document,
        Element,
        Instance,
        Link
    }

    internal class ExportFrame
    {
        public ExportFrameType Type;
        public ExportFrame Parent;
        public ElementId ParentId = ElementId.InvalidElementId; // Logical parent ID (SuperComponent or Host Element)
        public NodeBuilder Node;
        public string NodeName;
        public Matrix4x4 LocalMatrix = Matrix4x4.Identity;

        public MeshBuilderType MeshBuilder;
        public ElementId ElementId;
        public ElementId SymbolId = ElementId.InvalidElementId;
        public ElementId MaterialId = ElementId.InvalidElementId;

        public void EnsureMeshBuilder()
        {
            if (MeshBuilder == null) MeshBuilder = new MeshBuilderType();
        }
    }
}
