using Autodesk.Revit.DB;

namespace RevitTrueGltf
{
    internal class ElementInfo
    {
        public ElementId MaterialId { get; set; }

        public ElementId Id { get; set; }

        public ElementId SymbolId { get; set; }

        public bool ShareMesh { get; set; }

        public void Reset()
        {
            MaterialId = ElementId.InvalidElementId;
            Id = ElementId.InvalidElementId;
            SymbolId = ElementId.InvalidElementId;
            ShareMesh = false;
        }
    }
}
