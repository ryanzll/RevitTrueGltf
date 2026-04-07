namespace RevitTrueGltf.Models
{
    public class ParameterDTO
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public string GroupName { get; set; }
        public string GroupKey { get; set; }
        public string UniqueKey { get; set; } // Parameter Identifier (BuiltInName or GUID)
        public ExportDataType DataType { get; set; }
        public ParameterSource Source { get; set; }
        public string UnitType { get; set; } // Physical unit (e.g., UT_Length)
    }
}
