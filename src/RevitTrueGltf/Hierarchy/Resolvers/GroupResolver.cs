using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace RevitTrueGltf.Hierarchy.Resolvers
{
    /// <summary>
    /// Identifies all members of a Model Group as its children.
    /// </summary>
    public class GroupResolver : IHierarchyResolver
    {
        public bool CanHandle(Element element)
        {
            return element is Group;
        }

        public IEnumerable<ElementId> GetChildren(Element element)
        {
            if (element is Group group)
            {
                return group.GetMemberIds();
            }
            return null;
        }

        public ElementId GetParent(Element element)
        {
            return ElementId.InvalidElementId;
        }
    }
}
