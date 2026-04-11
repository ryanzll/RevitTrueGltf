using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace RevitTrueGltf.Hierarchy.Resolvers
{
    /// <summary>
    /// Identifies nested family components as children.
    /// </summary>
    public class FamilyInstanceResolver : IHierarchyResolver
    {
        public bool CanHandle(Element element)
        {
            return element is FamilyInstance;
        }

        public IEnumerable<ElementId> GetChildren(Element element)
        {
            if (element is FamilyInstance famInst)
            {
                return famInst.GetSubComponentIds();
            }
            return null;
        }

        public ElementId GetParent(Element element)
        {
            if (element is FamilyInstance famInst && famInst.SuperComponent != null)
            {
                return famInst.SuperComponent.Id;
            }
            return ElementId.InvalidElementId;
        }
    }
}
