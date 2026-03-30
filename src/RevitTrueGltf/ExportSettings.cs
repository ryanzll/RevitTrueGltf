namespace RevitTrueGltf
{
    /// <summary>
    /// Holds all user-configurable options for the glTF export pipeline.
    /// New options should be added here so they are decoupled from both the
    /// export context (geometry traversal) and the command (UI/Revit entry point).
    /// </summary>
    public class ExportSettings
    {
        // ── Geometry optimisation ──────────────────────────────────────────────

        /// <summary>
        /// When <c>true</c>, the exported file is post-processed with
        /// <b>gltfpack</b> (Meshoptimizer) to apply mesh quantization, vertex
        /// cache optimization, and index-buffer compression
        /// (<c>EXT_meshopt_compression</c>).
        /// Requires the <c>EveryBIM.gltfpack.Binaries</c> NuGet package.
        /// </summary>
        public bool UseMeshoptimizer { get; set; } = false;

        // ── Texture compression ────────────────────────────────────────────────

        /// <summary>
        /// When <c>true</c>, textures are transcoded to
        /// <b>Basis Universal / KTX2</b> format (<c>KHR_texture_basisu</c>)
        /// during export, significantly reducing GPU memory consumption.
        /// Requires the <c>EveryBIM.gltfpack.Binaries</c> NuGet package
        /// (gltfpack handles the transcoding step).
        /// </summary>
        public bool UseKtx2TextureCompression { get; set; } = false;

        // ── Future options (placeholders) ──────────────────────────────────────
        // Add new export options here. Each option should be self-contained and
        // documented with its effect, required dependencies, and default value.
        //
        // Example:
        //   /// <summary>Merge all elements into a single mesh node.</summary>
        //   public bool MergeAllMeshes { get; set; } = false;
    }
}
