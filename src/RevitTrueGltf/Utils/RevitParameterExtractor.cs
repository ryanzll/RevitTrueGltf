using Autodesk.Revit.DB;
using RevitTrueGltf.Models;
using System.Collections.Generic;

namespace RevitTrueGltf.Utils
{
    /// <summary>
    /// Utility class for extracting BIM parameters from Revit elements.
    /// Supports both Instance and Type parameters.
    /// </summary>
    public static class RevitParameterExtractor
    {
        public static ElementDataDTO ExtractParameters(Document document, Element element)
        {
            if (element == null) return null;

            var dto = new ElementDataDTO
            {
                ElementId = (int)element.Id.GetIdValue(),
                UniqueId = element.UniqueId,
                Name = element.Name,
                CategoryName = element.Category?.Name ?? "None",
                CategoryKey = RevitApiWrapper.GetCategoryKey(element.Category)
            };

            // 1. Instance Parameters
            foreach (Parameter param in element.Parameters)
            {
                if (!param.HasValue) continue;
                dto.Parameters.Add(MapParameter(param));
            }

            // 2. Type Parameters (Temporarily disabled to avoid potential ID collisions and simplify export)
            /*
            var typeId = element.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                var typeElement = document.GetElement(typeId);
                if (typeElement != null)
                {
                    foreach (Parameter param in typeElement.Parameters)
                    {
                        if (!param.HasValue) continue;
                        dto.Parameters.Add(MapParameter(param));
                    }
                }
            }
            */

            return dto;
        }

        private static ParameterDTO MapParameter(Parameter param)
        {
            var def = param.Definition;
            var dto = new ParameterDTO
            {
                Name = def.Name,
                GroupName = RevitApiWrapper.GetParameterGroupName(def),
                GroupKey = RevitApiWrapper.GetParameterGroupKey(def),
                UnitType = RevitApiWrapper.GetParameterUnitType(def),
                Source = param.IsShared ? ParameterSource.Shared : ParameterSource.BuiltIn
            };

            // Set true data type and extract value
            switch (param.StorageType)
            {
                case StorageType.Double:
                    dto.Value = param.AsDouble().ToString();
                    dto.DataType = ExportDataType.Double;
                    break;
                case StorageType.Integer:
                    dto.Value = param.AsInteger().ToString();
                    dto.DataType = ExportDataType.Int;
                    break;
                case StorageType.String:
                    dto.Value = param.AsString();
                    dto.DataType = ExportDataType.String;
                    break;
                case StorageType.ElementId:
                    dto.Value = param.AsElementId().GetIdValue().ToString();
                    dto.DataType = ExportDataType.Int; // ElementId is fundamentally an integer
                    break;
            }

            // Fallback: If value is still empty, try formatted string
            if (string.IsNullOrEmpty(dto.Value))
            {
                dto.Value = param.AsValueString() ?? "";
                dto.DataType = ExportDataType.String;
            }

            // Fallback for UniqueKey
            if (param.IsShared)
            {
                dto.UniqueKey = param.GUID.ToString();
            }
            else
            {
                var biParam = (param.Definition as InternalDefinition)?.BuiltInParameter;
                dto.UniqueKey = biParam?.ToString() ?? def.Name;
            }

            return dto;
        }
    }
}
