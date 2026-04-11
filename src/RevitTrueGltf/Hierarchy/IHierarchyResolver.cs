using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace RevitTrueGltf.Hierarchy
{
    /// <summary>
    /// Strategy interface for identifying child elements for a given Revit element.
    /// </summary>
    public interface IHierarchyResolver
    {
        /// <summary>
        /// Determines if this resolver can handle the specified element.
        /// </summary>
        bool CanHandle(Element element);

        /// <summary>
        /// Returns the collection of sub-element IDs that should be considered children of this element.
        /// </summary>
        IEnumerable<ElementId> GetChildren(Element element);

        /// <summary>
        /// Attempts to find the parent of the specified element (e.g., via SuperComponent or Host).
        /// </summary>
        ElementId GetParent(Element element);
    }
}
