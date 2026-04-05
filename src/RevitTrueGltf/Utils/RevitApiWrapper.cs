using Autodesk.Revit.DB;

namespace RevitTrueGltf.Utils
{
    /// <summary>
    /// Bridge for Revit API differences between different versions (2020-2026).
    /// Centralizes all #if preprocessor directives to keep business logic clean.
    /// </summary>
    public static class RevitApiWrapper
    {
        /// <summary>
        /// Gets the persistent ID value (Long in 2024+, Int in 2020-2023).
        /// </summary>
        public static long GetIdValue(this ElementId id)
        {
            if (id == null) return -1;
#if REVIT2024 || REVIT2025 || REVIT2026
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }


        /// <summary>
        /// Safely extracts the SymbolId from an InstanceNode.
        /// In modern Revit, this acts as an extension method.
        /// </summary>
        public static ElementId GetSymbolId(this InstanceNode node)
        {
            if (node == null) return ElementId.InvalidElementId;
#if REVIT2024 || REVIT2025 || REVIT2026
            return node.GetSymbolGeometryId().SymbolId;
#else
            return node.GetSymbolId();
#endif
        }

    }
}
