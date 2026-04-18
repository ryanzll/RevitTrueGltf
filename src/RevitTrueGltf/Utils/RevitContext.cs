using Autodesk.Revit.ApplicationServices;

namespace RevitTrueGltf.Utils
{
    /// <summary>
    /// Lightweight static class that stores Revit application-level runtime context.
    /// Must be initialized once at plugin startup (in ExportGltfCommand) before any
    /// export session begins. Provides version information to components that cannot
    /// easily access the Revit Application object directly (e.g. strategy classes).
    /// </summary>
    public static class RevitContext
    {
        /// <summary>
        /// The Revit application version number (e.g. "2024").
        /// Used for texture library path resolution via the Windows registry.
        /// </summary>
        public static string VersionNumber { get; private set; } = string.Empty;

        /// <summary>
        /// Initializes the static context from the Revit Application object.
        /// Call this once from ExportGltfCommand before creating any ExportGltfContext.
        /// </summary>
        public static void Initialize(Application app)
        {
            VersionNumber = app.VersionNumber;
        }
    }
}
