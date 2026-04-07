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
        public List<ParameterDTO> Parameters { get; set; } = new List<ParameterDTO>();
    }
}
