using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace RevitTrueGltf.Hierarchy.Resolvers
{
    /// <summary>
    /// Identifies Curtain Panels and Mullions as children of a Curtain Wall.
    /// </summary>
    public class CurtainWallResolver : IHierarchyResolver
    {
        public bool CanHandle(Element element)
        {
            if (element is Wall wall)
            {
                return wall.WallType.Kind == WallKind.Curtain;
            }
            return false;
        }

        public IEnumerable<ElementId> GetChildren(Element element)
        {
            var wall = element as Wall;
            var grid = wall.CurtainGrid;
            if (grid == null) return Enumerable.Empty<ElementId>();

            // Combine Panels and Mullions into a single enumerable
            return grid.GetPanelIds().Concat(grid.GetMullionIds());
        }

        public ElementId GetParent(Element element)
        {
            return ElementId.InvalidElementId;
        }
    }
}
