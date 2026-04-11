using Autodesk.Revit.DB;
using RevitTrueGltf.Hierarchy.Resolvers;
using System.Collections.Generic;
using System.Linq;

namespace RevitTrueGltf.Hierarchy
{
    public struct HierarchyResult
    {
        public ElementId ParentId;
        public IEnumerable<ElementId> Children;
    }

    /// <summary>
    /// Orchestrates hierarchy discovery. Returns raw findings for the caller to handle.
    /// </summary>
    public class HierarchyManager
    {
        private readonly List<IHierarchyResolver> _resolvers = new List<IHierarchyResolver>();

        public HierarchyManager()
        {
            _resolvers.Add(new FamilyInstanceResolver());
            _resolvers.Add(new CurtainWallResolver());
            _resolvers.Add(new GroupResolver());
        }

        /// <summary>
        /// Analyzes the element and returns its hierarchical findings in a single pass.
        /// </summary>
        public HierarchyResult Resolve(Element element)
        {
            var result = new HierarchyResult
            {
                ParentId = ElementId.InvalidElementId,
                Children = Enumerable.Empty<ElementId>()
            };

            if (element == null) return result;

            foreach (var resolver in _resolvers)
            {
                if (resolver.CanHandle(element))
                {
                    // Discovery: Get both parent link and sub-elements in one go
                    result.Children = resolver.GetChildren(element) ?? Enumerable.Empty<ElementId>();
                    result.ParentId = resolver.GetParent(element) ?? ElementId.InvalidElementId;
                    
                    // Stop at the first resolver that represents this element's type
                    break;
                }
            }

            return result;
        }
    }
}
