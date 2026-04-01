using System.Collections.Generic;

namespace RevitTrueGltf
{
    public enum MaterialMode { Texture, ColorOnly }
    public enum VertexPrecision { Standard = 14, High = 16, VeryHigh = 18 }
    public enum ExportPreset { Custom, Draft, Balanced, HighFidelity }

    public class ExportSettings
    {
        // --- 1. Export Scope ---
        public bool ExportFloors { get; set; } = true;
        public bool VisibleElementsOnly { get; set; } = true;
        public bool ExportBimProperties { get; set; } = true;
        public bool IncludeLinkedModels { get; set; } = true;

        // --- 2. Appearance ---
        public MaterialMode MaterialExportMode { get; set; } = MaterialMode.Texture;

        // --- 3. Optimization (gltfpack) ---
        public bool UseKtx2TextureCompression { get; set; } = false;
        public int TextureQuality { get; set; } = 8;
        public bool UseMeshoptimizer { get; set; } = false;
        public bool UseCustomVertexPrecision { get; set; } = false;
        public VertexPrecision VertexPositionPrecision { get; set; } = VertexPrecision.High;
        public double SimplificationRatio { get; set; } = 0.0; 

        // --- 4. Output Path ---
        public string ExportFilePath { get; set; } = string.Empty;

    }
}
