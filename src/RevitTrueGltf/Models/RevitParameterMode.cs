namespace RevitTrueGltf.Models
{
    public enum RevitParameterMode
    {
        FlatEmbedded,       // Flat embedded in extras of each node
        SchemaReferenced,   // Global Schema dictionary reference
        ExternalSqlite      // External SQLite database
    }
}
