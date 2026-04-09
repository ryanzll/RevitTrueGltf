using Autodesk.Revit.DB;
using RevitTrueGltf.ExportStrategies;
using RevitTrueGltf.Models;
using RevitTrueGltf.Utils;
using Serilog;
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

    /// <summary>
    /// Custom exporter context for converting Revit models to glTF scenes.
    /// 
    /// GRAPH HIERARCHY DESIGN & GLTF LIMITATIONS:
    /// 
    /// 1. The Matrix/Transform Limitation:
    ///    In the glTF specification, a single Node can possess at most ONE local transform matrix.
    ///    However, a Revit element (especially composed families or SuperComponents) might contain 
    ///    multiple instances, each requiring its own unique spatial transform.
    ///    
    /// 2. The Solution (Zero-Transform Element Nodes):
    ///    To safely support multiple instantiated pieces under a single element, and to prevent 
    ///    "Double Transforms" caused by appending world-coordinate meshes into offset parent nodes,
    ///    we utilize a strict "Zero-Transform" rule for architectural nodes.
    ///    
    ///    - Element Node: Represents the logical container for a Revit Element. It ALWAYS 
    ///                    retains an Identity Matrix (0,0,0 coordinate offset). It serves exclusively 
    ///                    as an anchor for BIM properties and a folder for visual components.
    ///    - Geometry/Instance Child Nodes: All physical Meshes are attached to explicit, named sub-nodes 
    ///                    ("Geometry" for direct meshes, "Instance" for transformed family instances).
    ///                    These child nodes carry the actual physical offsets (if any).
    /// 
    /// 3. Clear Viewer Topology (Babylon.js/Three.js):
    ///    This yields an extremely clean and uniform structure in web viewers:
    ///    [Root Node] -> [Element Node (Identity Matrix)] -> [Mesh Sub-Node (Transform & Geometry)]
    ///    This consistency makes parsing, hiding, and interacting with elements predictable for downstream developers.
    ///    
    ///    Example of the resulting node tree in a WebGL viewer (e.g. Babylon.js):
    ///    
    ///    📦 ElementNode (e.g. "Basic Wall [123456]")
    ///     ┣ 📂 Geometry (For direct geometries like walls/floors)
    ///     ┃  ┗ 📜 Geometry_primitive0
    ///     ┃
    ///     ┗ 📂 Instance (For family instances like doors/windows)
    ///        ┣ 📜 Instance_primitive0
    ///        ┗ 📜 Instance_primitive1
    /// </summary>
    internal class ExportGltfContext : IExportContext
    {
        // Required to track Revit's hierarchical Begin/End stream (Link->Element->Instance). 
        // This allows us to correctly nest nodes and resolve parent-child relationships in the glTF scene.
        private readonly Stack<ExportFrame> _stack = new Stack<ExportFrame>();

        // Provides access to the immediate caller's context for current geometry or element processing.
        private ExportFrame CurrentFrame => _stack.Count > 0 ? _stack.Peek() : null;

        private SceneBuilder _sceneBuilder = new SceneBuilder();
        private readonly Dictionary<ElementId, MeshBuilderType> _meshBuilderCache = new Dictionary<ElementId, MeshBuilderType>();
        private readonly Dictionary<ElementId, MaterialBuilder> _materialBuilderCache = new Dictionary<ElementId, MaterialBuilder>();

        private NodeBuilder _rootNode;
        private readonly Dictionary<ElementId, NodeBuilder> _elementNodeMap = new Dictionary<ElementId, NodeBuilder>();

        private Document _document = null;
        private ExportSettings _settings = null;

        private IParameterExportStrategy _parameterStrategy;

        public ExportGltfContext(Document doc, ExportSettings settings)
        {
            _document = doc;
            _settings = settings;
        }

        #region IExportContext
        public void Finish()
        {
            Log.Information("ExportGltfContext.Finish() triggered.");
            if (_parameterStrategy != null)
            {
                _parameterStrategy.FinalizeExport();
            }

            var model = _sceneBuilder.ToGltf2();

            Log.Information("Writing glTF file to: {ExportFilePath}", _settings.ExportFilePath);
            new GltfWriter(_settings).Write(model, _settings.ExportFilePath);
            Log.Information("Export process completed successfully.");
        }

        public bool IsCanceled()
        {
            return false;
        }

        public RenderNodeAction OnElementBegin(ElementId elementId)
        {
            var element = _document.GetElement(elementId);
            // Prepare the Element Frame (Delay node creation until mesh or child exists)
            var elementName = element?.Name;
            var elementFrame = new ExportFrame
            {
                Type = ExportFrameType.Element,
                Parent = CurrentFrame,
                NodeName = $"{elementName} [{elementId.GetIdValue()}]",
                ElementId = elementId
            };
            _stack.Push(elementFrame);
            Log.Debug("[Stack] Pushed Element: {Name} [{Id}]. Current Depth: {Count}",
                elementFrame.NodeName, elementId.GetIdValue(), _stack.Count);

            if (element == null) return RenderNodeAction.Skip;

            Log.Debug("OnElementBegin: {Name} [{Id}], Category: {Category}",
                element.Name, elementId.GetIdValue(), element.Category?.Name);

            // Skip hidden elements if only visible elements are requested
            if (_settings != null && _settings.VisibleElementsOnly)
            {
                if (element.IsHidden(_document.ActiveView)) return RenderNodeAction.Skip;
            }

            // Skip floors if not requested
            if (_settings != null && !_settings.ExportFloors)
            {
                if (element.Category != null && element.Category.Id.GetIdValue() == (long)BuiltInCategory.OST_Floors)
                {
                    return RenderNodeAction.Skip;
                }
            }

            // Set logical parent if this is a SubComponent
            var famInst = element as FamilyInstance;
            if (famInst != null && famInst.SuperComponent != null)
            {
                elementFrame.ParentId = famInst.SuperComponent.Id;
            }

            // Important: If we are re-entering an element already created via recursion, 
            // ensure we don't duplicate its node mapping.
            if (_elementNodeMap.TryGetValue(elementId, out var existingNode))
            {
                elementFrame.Node = existingNode;
            }

            return RenderNodeAction.Proceed;
        }

        public void OnElementEnd(ElementId elementId)
        {
            if (_stack.Count == 0)
            {
                Log.Warning("[Stack] OnElementEnd for {Id} called with EMPTY stack!", elementId.GetIdValue());
                return;
            }
            var frame = _stack.Pop();
            Log.Debug("[Stack] Popped {Type} (expected Element): {Name}. Current Depth: {Count}",
                frame.Type, frame.NodeName, _stack.Count);

            if (frame.MeshBuilder != null && frame.MeshBuilder.Primitives.Count > 0)
            {
                // Ensure the node exists before adding direct geometry
                var node = GetOrCreateGltfNode(frame);

                // Assign mesh to an explicitly named child node for structural clarity
                var geomNode = node.CreateNode("Geometry");
                var instance = _sceneBuilder.AddRigidMesh(frame.MeshBuilder, geomNode);
            }

            // Update the map if the node was actually created
            if (frame.Node != null)
            {
                // Handle Parameters
                if (_parameterStrategy != null)
                {
                    var element = _document.GetElement(elementId);
                    var dto = RevitParameterExtractor.ExtractParameters(_document, element);
                    _parameterStrategy.OnElement(dto, frame.Node);
                }

                if (!_elementNodeMap.ContainsKey(elementId))
                {
                    _elementNodeMap[elementId] = frame.Node;
                }
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
            var symbolId = node.GetSymbolId();

            var parentFrame = CurrentFrame;
            string nodeName = string.IsNullOrEmpty(node.NodeName) ? "Instance" : node.NodeName;
            if (nodeName.StartsWith("RNT_")) nodeName = "Instance";
            // Prepare lazy frame
            var instanceFrame = new ExportFrame
            {
                Type = ExportFrameType.Instance,
                Parent = parentFrame,
                ParentId = parentFrame.Type == ExportFrameType.Element ? parentFrame.ElementId : ElementId.InvalidElementId,
                ElementId = parentFrame.ElementId,
                SymbolId = symbolId,
                MaterialId = parentFrame.MaterialId,
                NodeName = nodeName
            };
            _stack.Push(instanceFrame);
            Log.Debug("[Stack] Pushed Instance: {Name} (Symbol: {SymbolId}). Current Depth: {Count}",
                instanceFrame.NodeName, symbolId.GetIdValue(), _stack.Count);

            if (parentFrame == null) return RenderNodeAction.Skip;

            var transformMatrix = ConvertTransform(node.GetTransform());
            instanceFrame.LocalMatrix = transformMatrix;

            if (_meshBuilderCache.TryGetValue(symbolId, out var cachedMesh))
            {
                if (cachedMesh.Primitives.Count > 0)
                {
                    // For cached mesh, we MUST create the node now
                    var instanceNode = GetOrCreateGltfNode(instanceFrame);
                    var instance = _sceneBuilder.AddRigidMesh(cachedMesh, instanceNode);
                }
                return RenderNodeAction.Skip;
            }

            instanceFrame.EnsureMeshBuilder();
            return RenderNodeAction.Proceed;
        }

        public void OnInstanceEnd(InstanceNode node)
        {
            if (_stack.Count == 0)
            {
                Log.Warning("[Stack] OnInstanceEnd called with EMPTY stack!");
                return;
            }
            var frame = _stack.Pop();
            Log.Debug("[Stack] Popped {Type} (expected Instance): {Name}. Current Depth: {Count}",
                frame.Type, frame.NodeName, _stack.Count);

            // Only process if this frame was pushed by OnInstanceBegin (Symbol capture)
            if (frame != null && frame.SymbolId != ElementId.InvalidElementId)
            {
                if (frame.MeshBuilder != null && frame.MeshBuilder.Primitives.Count > 0)
                {
                    // Cache the captured mesh if not already cached
                    if (!_meshBuilderCache.ContainsKey(frame.SymbolId))
                    {
                        _meshBuilderCache.Add(frame.SymbolId, frame.MeshBuilder);
                    }

                    // Add it to the scene attached to the instance node (lazily created if needed)
                    var gltfNode = GetOrCreateGltfNode(frame);
                    var instance = _sceneBuilder.AddRigidMesh(frame.MeshBuilder, gltfNode);
                }
            }
        }

        public void OnLight(LightNode node)
        {
        }

        public RenderNodeAction OnLinkBegin(LinkNode node)
        {
            var linkDoc = node.GetDocument();
            var linkFrame = new ExportFrame
            {
                Type = ExportFrameType.Link,
                Parent = CurrentFrame,
                NodeName = linkDoc?.Title ?? "Linked Model",
                LocalMatrix = ConvertTransform(node.GetTransform())
            };

            _stack.Push(linkFrame);
            Log.Debug("[Stack] Pushed Link: {Name}. Current Depth: {Count}",
                linkFrame.NodeName, _stack.Count);
            return RenderNodeAction.Proceed;
        }

        public void OnLinkEnd(LinkNode node)
        {
            if (_stack.Count > 0)
            {
                var frame = _stack.Pop();
                Log.Debug("[Stack] Popped {Type} (expected Link): {Name}. Current Depth: {Count}",
                    frame.Type, frame.NodeName, _stack.Count);
            }
            else
            {
                Log.Warning("[Stack] OnLinkEnd called with EMPTY stack!");
            }
        }

        public void OnMaterial(MaterialNode node)
        {
            try
            {
                var frame = CurrentFrame;
                if (frame == null) return;

                frame.MaterialId = node.MaterialId;
                if (!_materialBuilderCache.ContainsKey(frame.MaterialId))
                {
                    MaterialUtils materialUtils = new MaterialUtils();
                    var materialBuilder = new MaterialBuilder(node.NodeName);
                    if (!materialUtils.Convert(node, materialBuilder))
                    {
                        var color = node.Color;
                        var transparency = node.Transparency;
                        materialBuilder = new MaterialBuilder(node.NodeName).
                            WithBaseColor(new Vector4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, (float)(1.0f - transparency)));
                        if (transparency > 0)
                        {
                            materialBuilder.WithAlpha(SharpGLTF.Materials.AlphaMode.BLEND);
                        }
                    }
                    _materialBuilderCache.Add(frame.MaterialId, materialBuilder);
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        public void OnPolymesh(PolymeshTopology node)
        {
            try
            {
                var frame = CurrentFrame;
                if (frame == null) return;

                MaterialBuilder materialBuilder = null;
                if (!_materialBuilderCache.TryGetValue(frame.MaterialId, out materialBuilder))
                {
                    materialBuilder = new MaterialBuilder("Default").WithBaseColor(new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
                }

                frame.EnsureMeshBuilder();
                var primitive = frame.MeshBuilder.UsePrimitive(materialBuilder);

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

                    // Skip degenerate triangles - read positions directly from the vertex structs
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
            catch (Exception)
            {
                throw;
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
            Log.Information("ExportGltfContext.Start() initialized for project.");
            // 1. Initialize the project root node
            string projectName = _document.ProjectInformation.Name;
            _rootNode = new NodeBuilder(string.IsNullOrEmpty(projectName) ? "Revit Model" : projectName);
            _sceneBuilder.AddNode(_rootNode);

            // 2. Initialize Parameter Strategy
            if (_settings.ExportRevitParameters)
            {
                switch (_settings.RevitParameterMode)
                {
                    case RevitParameterMode.FlatEmbedded:
                        _parameterStrategy = new FlatEmbeddedStrategy();
                        break;
                    case RevitParameterMode.SchemaReferenced:
                        _parameterStrategy = new SchemaReferencedStrategy();
                        break;
                    case RevitParameterMode.ExternalSqlite:
                        _parameterStrategy = new SqliteExternalStrategy();
                        break;
                    default:
                        _parameterStrategy = new FlatEmbeddedStrategy();
                        break;
                }

                _parameterStrategy.Initialize(_settings.ExportFilePath, _rootNode);
            }

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

        #region Frame
        private NodeBuilder GetOrCreateGltfNode(ExportFrame frame)
        {
            if (frame.Node != null) return frame.Node;

            NodeBuilder parentNode;
            if (frame.Parent != null)
            {
                // 1. Priority: Immediate stack parent (for Link/Instance nesting)
                parentNode = GetOrCreateGltfNode(frame.Parent);
            }
            else
            {
                // 2. Fallback: Logical parent ID (for flattened SuperComponent hierarchy)
                parentNode = GetOrCreateElementNode(frame.ParentId);
            }

            frame.Node = parentNode.CreateNode(frame.NodeName);
            frame.Node.LocalMatrix = frame.LocalMatrix;

            // Register this node in the map immediately to make it available for its own SubComponents
            if (frame.ElementId != ElementId.InvalidElementId && !_elementNodeMap.ContainsKey(frame.ElementId))
            {
                _elementNodeMap[frame.ElementId] = frame.Node;
            }

            return frame.Node;
        }

        /// <summary>
        /// Recursively ensures a logical Node exists for a given element ID.
        /// Rebuilds SuperComponent hierarchies correctly even if they were processed earlier in the stream.
        /// </summary>
        private NodeBuilder GetOrCreateElementNode(ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return _rootNode;
            if (_elementNodeMap.TryGetValue(id, out var node)) return node;

            var element = _document.GetElement(id);
            if (element == null) return _rootNode;

            // Resolve logical parent (SuperComponent)
            ElementId parentId = ElementId.InvalidElementId;
            if (element is FamilyInstance famInst && famInst.SuperComponent != null)
            {
                parentId = famInst.SuperComponent.Id;
            }

            // Recursive climb to the root of the SuperComponent chain
            var parentNode = GetOrCreateElementNode(parentId);

            var newNodeName = $"{element.Name} [{id.GetIdValue()}]";
            var newNode = parentNode.CreateNode(newNodeName);
            _elementNodeMap[id] = newNode;
            return newNode;
        }
        #endregion

        /// <summary>
        /// Returns the accumulated scene so the caller can decide how to write
        /// (plain save, gltfpack post-process, etc.).
        /// </summary>
        public SceneBuilder ToScene() => _sceneBuilder;
    }
}
