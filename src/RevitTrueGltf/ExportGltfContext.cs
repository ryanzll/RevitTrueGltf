using Autodesk.Revit.DB;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using System.Collections.Generic;
using System.Numerics;

namespace RevitTrueGltf
{
    using MeshBuilderType = MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>;
    using VertexBuilderType = VertexBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>;

    internal class ExportGltfContext : IExportContext
    {
        private SceneBuilder _sceneBuilder = new SceneBuilder();
        private MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty> _meshBuilder = new MeshBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>();
        private readonly Dictionary<ElementId, MeshBuilderType> _meshBuilderCache = new Dictionary<ElementId, MeshBuilderType>();
        private readonly Dictionary<ElementId, MaterialBuilder> _materialBuilderCache = new Dictionary<ElementId, MaterialBuilder>();

        private ElementInfo _elementInfo = new ElementInfo();
        private Document _document = null;

        public ExportGltfContext(Document doc)
        {
            _document = doc;
        }

        #region IExportContext
        public void Finish()
        {
        }

        public bool IsCanceled()
        {
            return false;
        }

        public RenderNodeAction OnElementBegin(ElementId elementId)
        {
            _elementInfo.Reset();
            var element = _document.GetElement(elementId);
            if (element == null)
            {
                return RenderNodeAction.Skip;
            }

            if (element is Family || element is FamilySymbol)
            {
                return RenderNodeAction.Skip;
            }

            _elementInfo.Id = elementId;
            _meshBuilder = new MeshBuilderType();
            return RenderNodeAction.Proceed;
        }

        public void OnElementEnd(ElementId elementId)
        {
            if (_meshBuilder != null)
            {
                _sceneBuilder.AddRigidMesh(_meshBuilder, Matrix4x4.Identity);
                _meshBuilder = null;
            }
        }

        public RenderNodeAction OnFaceBegin(FaceNode node)
        {
            return RenderNodeAction.Proceed;
        }

        public void OnFaceEnd(FaceNode node)
        {
        }

        public RenderNodeAction OnInstanceBegin(InstanceNode node)
        {
            _elementInfo.SymbolId = node.GetSymbolId();
            if (_meshBuilderCache.ContainsKey(_elementInfo.SymbolId))
            {
                return RenderNodeAction.Skip;
            }

            return RenderNodeAction.Proceed;
        }

        public void OnInstanceEnd(InstanceNode node)
        {
            if (_elementInfo.SymbolId != ElementId.InvalidElementId &&
                _elementInfo.SymbolId == node.GetSymbolId() &&
                _meshBuilder != null)
            {
                MeshBuilderType meshBuilder = null;
                var transformMatrix = ConvertTransform(node.GetTransform());
                if (_meshBuilderCache.TryGetValue(_elementInfo.SymbolId, out meshBuilder))
                {
                    _sceneBuilder.AddRigidMesh(meshBuilder, transformMatrix);
                }
                else
                {
                    _meshBuilderCache.Add(_elementInfo.SymbolId, _meshBuilder);
                    _sceneBuilder.AddRigidMesh(_meshBuilder, transformMatrix);
                }
                _meshBuilder = null;
            }
        }

        public void OnLight(LightNode node)
        {
        }

        public RenderNodeAction OnLinkBegin(LinkNode node)
        {
            return RenderNodeAction.Proceed;
        }

        public void OnLinkEnd(LinkNode node)
        {
        }

        public void OnMaterial(MaterialNode node)
        {
            _elementInfo.MaterialId = node.MaterialId;
            MaterialBuilder materialBuilder = null;
            if (!_materialBuilderCache.TryGetValue(_elementInfo.MaterialId, out materialBuilder))
            {
                var color = node.Color;
                var transparency = node.Transparency;
                materialBuilder = new MaterialBuilder(node.NodeName).
                    WithBaseColor(new Vector4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, (float)(1.0f - transparency)));
                if (transparency > 0)
                {
                    materialBuilder.WithAlpha(AlphaMode.BLEND);
                }

                _materialBuilderCache.Add(_elementInfo.MaterialId, materialBuilder);
            }
        }

        public void OnPolymesh(PolymeshTopology node)
        {
            MaterialBuilder materialBuilder = null;
            if (!_materialBuilderCache.TryGetValue(_elementInfo.MaterialId, out materialBuilder))
            {
                materialBuilder = new MaterialBuilder("Default").WithBaseColor(new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
            }

            // if a instance has nested instance and another mesh
            if (_meshBuilder == null)
            {
                _meshBuilder = new MeshBuilderType();
            }
            var primitive = _meshBuilder.UsePrimitive(materialBuilder);

            var points = node.GetPoints();
            var normals = node.GetNormals();
            int facetIndex = 0;
            foreach (var facet in node.GetFacets())
            {
                var v0 = CreateVertex(points[facet.V1], GetFaceVerexNormal(node, facetIndex, facet.V1));
                var v1 = CreateVertex(points[facet.V2], GetFaceVerexNormal(node, facetIndex, facet.V2));
                var v2 = CreateVertex(points[facet.V3], GetFaceVerexNormal(node, facetIndex, facet.V3));
                primitive.AddTriangle(v0, v2, v1);
                facetIndex++;
            }
        }

        public void OnRPC(RPCNode node)
        {
        }

        public RenderNodeAction OnViewBegin(ViewNode node)
        {
            return RenderNodeAction.Proceed;
        }

        public void OnViewEnd(ElementId elementId)
        {
        }

        public bool Start()
        {
            return true;
        }
        #endregion

        #region Utils
        private VertexBuilderType CreateVertex(XYZ point, XYZ normal)
        {
            var position = ConvertPt(point);
            var normalVec = ConvertNormal(normal);
            return new VertexBuilderType(new VertexPositionNormal { Position = position, Normal = normalVec });
        }

        private Vector3 ConvertPt(XYZ point)
        {
            return new Vector3(
                (float)UnitUtils.Feet2Meter(point.X),
                (float)UnitUtils.Feet2Meter(point.Z),   // Revit Z 变成 glTF Y
                -(float)UnitUtils.Feet2Meter(point.Y)   // Revit Y 变成 glTF -Z
            );
        }

        private Vector3 ConvertNormal(XYZ normal)
        {
            if (normal == null) return Vector3.UnitY;
            var vec = new Vector3((float)normal.X, (float)normal.Z, -(float)normal.Y);
            return Vector3.Normalize(vec);
        }

        private Matrix4x4 ConvertTransform(Transform t)
        {
            // 注意：变换矩阵的平移部分需要转换单位，旋转部分需要处理 Z-up 到 Y-up 的轴交换
            var right = ConvertPt(t.BasisX); // 这里为了保持矩阵正交，可能需要自定义逻辑，或者直接转换分量
            var up = ConvertPt(t.BasisZ);
            var forward = ConvertPt(t.BasisY);

            // 更简便安全的矩阵转换方式：
            return new Matrix4x4(
                (float)t.BasisX.X, (float)t.BasisX.Z, -(float)t.BasisX.Y, 0,
                (float)t.BasisZ.X, (float)t.BasisZ.Z, -(float)t.BasisZ.Y, 0, // Z 轴变 Y 轴
                -(float)t.BasisY.X, -(float)t.BasisY.Z, (float)t.BasisY.Y, 0, // Y 轴变 -Z 轴
                (float)UnitUtils.Feet2Meter(t.Origin.X),
                (float)UnitUtils.Feet2Meter(t.Origin.Z),
                -(float)UnitUtils.Feet2Meter(t.Origin.Y), 1
            );
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="node"></param>
        /// <param name="facetIndex"></param>
        /// <param name="vertexIndex">index of vertex in total vertex, not index in facet</param>
        /// <returns></returns>
        private XYZ GetFaceVerexNormal(PolymeshTopology node, int facetIndex, int vertexIndex)
        {
            if (node.DistributionOfNormals == DistributionOfNormals.OnePerFace)
            {
                if (node.NumberOfNormals <= 0)
                {
                    return XYZ.Zero;
                }
                return node.GetNormal(0);
            }
            else if (node.DistributionOfNormals == DistributionOfNormals.AtEachPoint)
            {
                if (vertexIndex >= node.NumberOfNormals)
                {
                    return XYZ.Zero;
                }
                return node.GetNormal(vertexIndex);
            }
            else if (node.DistributionOfNormals == DistributionOfNormals.OnEachFacet)
            {
                if (facetIndex >= node.NumberOfNormals)
                {
                    return XYZ.Zero;
                }
                return node.GetNormal(facetIndex);
            }

            return XYZ.Zero;
        }
        #endregion

        public void Save(string filePath)
        {
            var model = _sceneBuilder.ToGltf2();
            model.Save(filePath);
        }
    }
}
