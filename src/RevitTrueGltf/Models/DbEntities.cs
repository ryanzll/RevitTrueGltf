using Dapper.Contrib.Extensions;

namespace RevitTrueGltf.Models
{
    [Table("Categories")]
    public class DbCategory
    {
        [ExplicitKey] public int Id { get; set; }
        public string CategoryKey { get; set; }
        public string CategoryName { get; set; }
    }

    [Table("Elements")]
    public class DbElement
    {
        [ExplicitKey] public int Id { get; set; }
        public string UniqueId { get; set; }
        public int ElementId { get; set; }
        public int? CategoryId { get; set; }
    }

    [Table("ParameterDefinitions")]
    public class DbParameterDefinition
    {
        [ExplicitKey] public int Id { get; set; }
        public string UniqueKey { get; set; }
        public string DisplayName { get; set; }
        public int DataType { get; set; }
        public int Source { get; set; }
        public string GroupKey { get; set; }
        public string GroupName { get; set; }
        public string UnitType { get; set; }
    }

    [Table("ElementParameters")]
    public class DbElementParameter
    {
        // Composite key, direct Insert
        public int ElementId { get; set; }
        public int DefinitionId { get; set; }
        public string Value { get; set; }
    }
}
