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

        public static string GetCategoryKey(Category category)
        {
            if (category == null) return "None";
#if REVIT2024 || REVIT2025 || REVIT2026
            return ((BuiltInCategory)(int)category.Id.Value).ToString();
#else
            return ((BuiltInCategory)category.Id.IntegerValue).ToString();
#endif
        }

        public static string GetParameterGroupKey(Definition definition)
        {
#if REVIT2024 || REVIT2025 || REVIT2026
            return definition.GetGroupTypeId()?.TypeId ?? "";
#else
            return definition.ParameterGroup.ToString();
#endif
        }

        public static string GetParameterGroupName(Definition definition)
        {
#if REVIT2024 || REVIT2025 || REVIT2026
            var typeId = definition.GetGroupTypeId();
            return typeId != null ? LabelUtils.GetLabelForGroup(typeId) : "";
#else
            return LabelUtils.GetLabelFor(definition.ParameterGroup);
#endif
        }

        public static string GetParameterUnitType(Definition definition)
        {
            if (definition == null) return "None";
#if REVIT2021 || REVIT2022 || REVIT2023 || REVIT2024 || REVIT2025 || REVIT2026
            // UnitType concept was replaced by SpecTypeId/ForgeTypeId
            try {
                var typeId = definition.GetSpecTypeId();
                return typeId?.TypeId ?? "None";
            } catch { return "None"; }
#else
            // Legacy versions (Revit 2020)
            try {
                return definition.UnitType.ToString();
            } catch { return "None"; }
#endif
        }
    }
}
