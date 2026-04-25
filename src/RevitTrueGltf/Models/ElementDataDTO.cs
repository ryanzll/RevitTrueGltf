using System.Collections.Generic;

namespace RevitTrueGltf.Models
{
    public class ElementDataDTO
    {
        public int ElementId { get; set; }
        public string UniqueId { get; set; }
        public string Name { get; set; }
        public string CategoryName { get; set; }
        public string CategoryKey { get; set; }
        public string LevelName { get; set; } = "None";
        // UniqueId of the associated Revit Level element (stable GUID string). Empty string if unresolved.
        public string LevelUniqueId { get; set; } = "";
        public List<ParameterDTO> Parameters { get; set; } = new List<ParameterDTO>();
    }
}
