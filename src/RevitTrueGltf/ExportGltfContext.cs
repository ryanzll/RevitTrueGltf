using Autodesk.Revit.DB;
using RevitTrueGltf.Utils;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace RevitTrueGltf
{
    using MeshBuilderType = MeshBuilder<VertexPositionNormalTangent, VertexTexture1, VertexEmpty>;
    using VertexBuilderType = VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexEmpty>;

    internal class ExportGltfContext : IExportContext
    {
        private SceneBuilder _sceneBuilder = new SceneBuilder();
        private MeshBuilderType _meshBuilder = new MeshBuilderType();
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
#if REVIT2024 || REVIT2025 || REVIT2026
            _elementInfo.SymbolId = node.GetSymbolGeometryId().SymbolId;
#else
            _elementInfo.SymbolId = node.GetSymbolId();
#endif
            if (_meshBuilderCache.ContainsKey(_elementInfo.SymbolId))
            {
                return RenderNodeAction.Skip;
            }

            return RenderNodeAction.Proceed;
        }

        public void OnInstanceEnd(InstanceNode node)
        {
            if (_elementInfo.SymbolId != ElementId.InvalidElementId &&
#if REVIT2024 || REVIT2025 || REVIT2026
                _elementInfo.SymbolId == node.GetSymbolGeometryId().SymbolId &&
#else
                _elementInfo.SymbolId == node.GetSymbolId() &&
#endif
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
            try
            {
                _elementInfo.MaterialId = node.MaterialId;
                MaterialBuilder materialBuilder = null;
                if (!_materialBuilderCache.TryGetValue(_elementInfo.MaterialId, out materialBuilder))
                {
                    MaterialUtils materialUtils = new MaterialUtils();
                    materialBuilder = new MaterialBuilder(node.NodeName);
                    if (!materialUtils.Convert(node, materialBuilder))
                    {
                        var color = node.Color;
                        var transparency = node.Transparency;
                        materialBuilder = new MaterialBuilder(node.NodeName).
                            WithBaseColor(new Vector4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, (float)(1.0f - transparency)));
                        if (transparency > 0)
                        {
                            materialBuilder.WithAlpha(AlphaMode.BLEND);
                        }
                    }
                    _materialBuilderCache.Add(_elementInfo.MaterialId, materialBuilder);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public void OnPolymesh(PolymeshTopology node)
        {
            try
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
                var uvs = node.GetUVs();
                bool hasUVs = uvs != null && uvs.Count > 0;
                int facetIndex = 0;
                foreach (var facet in node.GetFacets())
                {
                    // Build full glTF vertices (position, normal, UV all converted; tangent set last)
                    var v0 = CreateVertex(points[facet.V1], GetFaceVerexNormal(node, facetIndex, facet.V1), hasUVs ? uvs[facet.V1] : new UV(0, 0));
                    var v1 = CreateVertex(points[facet.V2], GetFaceVerexNormal(node, facetIndex, facet.V2), hasUVs ? uvs[facet.V2] : new UV(0, 0));
                    var v2 = CreateVertex(points[facet.V3], GetFaceVerexNormal(node, facetIndex, facet.V3), hasUVs ? uvs[facet.V3] : new UV(0, 0));

                    // Skip degenerate triangles — read positions directly from the vertex structs
                    if (Vector3.DistanceSquared(v0.Geometry.Position, v1.Geometry.Position) < 1e-12f ||
                        Vector3.DistanceSquared(v1.Geometry.Position, v2.Geometry.Position) < 1e-12f ||
                        Vector3.DistanceSquared(v2.Geometry.Position, v0.Geometry.Position) < 1e-12f)
                    {
                        facetIndex++;
                        continue;
                    }

                    // Compute tangent from vertex positions, UVs, and face normal (all already in glTF space)
                    var tangent = ComputeTangent(
                        v0.Geometry.Position, v1.Geometry.Position, v2.Geometry.Position,
                        v0.Material.TexCoord, v1.Material.TexCoord, v2.Material.TexCoord,
                        v0.Geometry.Normal
                    );

                    // Patch tangent back into each vertex (Geometry is a value type, must get-modify-reassign)
                    var g0 = v0.Geometry; g0.Tangent = tangent; v0.Geometry = g0;
                    var g1 = v1.Geometry; g1.Tangent = tangent; v1.Geometry = g1;
                    var g2 = v2.Geometry; g2.Tangent = tangent; v2.Geometry = g2;

                    primitive.AddTriangle(v0, v1, v2);
                    facetIndex++;
                }
            }
            catch (Exception ex)
            {
                throw ex;
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
        /// <summary>
        /// Builds a glTF vertex from Revit-space inputs. Performs all coordinate conversions
        /// (ConvertPt, ConvertNormal, UV V-flip) internally. Tangent is left as default (zero)
        /// and must be patched in by the caller after computing it.
        /// </summary>
        private VertexBuilderType CreateVertex(XYZ point, XYZ normal, UV uv)
        {
            var geometry = new VertexPositionNormalTangent
            {
                Position = ConvertPt(point),
                Normal = ConvertNormal(normal)
                // Tangent intentionally left as default; caller sets it after ComputeTangent
            };
            // glTF UV origin is top-left (V increases downward); Revit UV origin is bottom-left
            var texture = new VertexTexture1 { TexCoord = new Vector2((float)uv.U, 1f - (float)uv.V) };
            return new VertexBuilderType(geometry, texture);
        }

        /// <summary>
        /// Computes a per-triangle tangent vector (with handedness W = ±1) from already-converted
        /// glTF-space positions, UVs, and face normal using the standard UV-derivative method.
        /// Applies Gram-Schmidt orthogonalization to ensure T ⊥ N (required for correct normal mapping
        /// on smooth-shaded meshes where vertex normals differ from the geometric face normal).
        /// Falls back to an arbitrary perpendicular tangent when UVs are degenerate.
        /// All inputs must be in glTF coordinate space; no conversion is performed here.
        /// </summary>
        private Vector4 ComputeTangent(Vector3 pos0, Vector3 pos1, Vector3 pos2, Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector3 faceNormal)
        {
            var edge1 = pos1 - pos0;
            var edge2 = pos2 - pos0;

            float du1 = uv1.X - uv0.X;
            float dv1 = uv1.Y - uv0.Y;
            float du2 = uv2.X - uv0.X;
            float dv2 = uv2.Y - uv0.Y;

            float det = du1 * dv2 - du2 * dv1;

            Vector3 tangent;
            float handedness = 1.0f;

            if (Math.Abs(det) < 1e-6f)
            {
                // Degenerate or missing UVs — build an arbitrary tangent perpendicular to the normal
                tangent = Vector3.Cross(faceNormal, Vector3.UnitZ);
                if (tangent.LengthSquared() < 0.1f)
                    tangent = Vector3.Cross(faceNormal, Vector3.UnitX);
                tangent = Vector3.Normalize(tangent);
            }
            else
            {
                float f = 1.0f / det;
                tangent = Vector3.Normalize(new Vector3(
                    f * (dv2 * edge1.X - dv1 * edge2.X),
                    f * (dv2 * edge1.Y - dv1 * edge2.Y),
                    f * (dv2 * edge1.Z - dv1 * edge2.Z)
                ));

                // Gram-Schmidt orthogonalization: re-orthogonalize T against N so that T ⊥ N.
                // UV-derived tangents are not guaranteed to be perpendicular to interpolated vertex
                // normals on smooth-shaded meshes, but normal mapping requires an orthonormal TBN basis.
                tangent = Vector3.Normalize(tangent - faceNormal * Vector3.Dot(faceNormal, tangent));

                // Compute UV-space bitangent, then check handedness (W = ±1).
                // Handedness is computed after orthogonalization using the original UV-derived bitangent.
                var bitangent = new Vector3(
                    f * (-du2 * edge1.X + du1 * edge2.X),
                    f * (-du2 * edge1.Y + du1 * edge2.Y),
                    f * (-du2 * edge1.Z + du1 * edge2.Z)
                );
                handedness = Vector3.Dot(Vector3.Cross(faceNormal, tangent), bitangent) < 0f ? -1.0f : 1.0f;
            }

            return new Vector4(tangent, handedness);
        }

        /// <summary>
        /// Converts Revit coordinates to glTF coordinates.
        /// 
        /// Coordinate Systems:
        /// 
        ///   Revit (Right-handed, Z-up)
        ///        Z (Up)
        ///        ^   Y (Back/North)
        ///        |  /
        ///        | /
        ///        o------> X (Right/East)
        ///
        ///   glTF (Right-handed, Y-up)
        ///        Y (Up)
        ///        ^
        ///        |
        ///        o------> X (Right)
        ///       /
        ///      v
        ///     Z (Forward/Out of screen)
        ///
        ///   Mapping:
        ///   glTF.X =  Revit.X
        ///   glTF.Y =  Revit.Z
        ///   glTF.Z = -Revit.Y
        /// </summary>
        private Vector3 ConvertPt(XYZ point)
        {
            return new Vector3(
                (float)UnitUtils.Feet2Meter(point.X),
                (float)UnitUtils.Feet2Meter(point.Z),   // Revit Z becomes glTF Y
                -(float)UnitUtils.Feet2Meter(point.Y)   // Revit Y becomes glTF -Z
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
            // Note: The translation part of the transformation matrix requires unit conversion, and the rotation part needs to handle the axis swap from Z-up to Y-up
            var right = ConvertPt(t.BasisX); // Custom logic might be needed here to keep the matrix orthogonal, or simply converting the components directly
            var up = ConvertPt(t.BasisZ);
            var forward = ConvertPt(t.BasisY);

            // A simpler and safer way to convert the matrix:
            return new Matrix4x4(
                (float)t.BasisX.X, (float)t.BasisX.Z, -(float)t.BasisX.Y, 0,
                (float)t.BasisZ.X, (float)t.BasisZ.Z, -(float)t.BasisZ.Y, 0, // Z axis becomes Y axis
                -(float)t.BasisY.X, -(float)t.BasisY.Z, (float)t.BasisY.Y, 0, // Y axis becomes -Z axis
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

        /// <summary>
        /// Returns the accumulated scene so the caller can decide how to write
        /// (plain save, gltfpack post-process, etc.).
        /// </summary>
        public SceneBuilder ToScene() => _sceneBuilder;
    }
}
