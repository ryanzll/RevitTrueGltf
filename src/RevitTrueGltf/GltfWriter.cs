using SharpGLTF.Schema2;
using SharpGLTF.Scenes;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace RevitTrueGltf
{
    /// <summary>
    /// Converts a <see cref="SceneBuilder"/> to a glTF/glb file on disk,
    /// optionally post-processing it with <b>gltfpack</b> based on the
    /// provided <see cref="ExportSettings"/>.
    ///
    /// This class is intentionally separate from <see cref="ExportGltfContext"/>
    /// whose sole responsibility is implementing Revit's <c>IExportContext</c>
    /// geometry callbacks.
    /// </summary>
    internal class GltfWriter
    {
        private readonly ExportSettings _settings;

        public GltfWriter(ExportSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Writes <paramref name="model"/> to <paramref name="filePath"/>.
        /// If any gltfpack options are enabled in <see cref="ExportSettings"/>,
        /// the file is first saved to a temp path and then processed by gltfpack.
        /// </summary>
        public void Write(ModelRoot model, string filePath)
        {
            // Check if we need to run gltfpack for optimization or compression
            bool needGltfpack = _settings.UseMeshoptimizer 
                             || _settings.UseKtx2TextureCompression 
                             || _settings.UseCustomVertexPrecision
                             || _settings.SimplificationRatio > 0;

            if (!needGltfpack)
            {
                model.Save(filePath);
                return;
            }

            // Save to a temp .glb first, then let gltfpack produce the final file.
            string tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".glb");
            try
            {
                model.Save(tempPath);
                RunGltfpack(tempPath, filePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Invokes the bundled <c>gltfpack</c> binary with the flags derived
        /// from <see cref="ExportSettings"/> and waits for it to finish.
        /// </summary>
        private void RunGltfpack(string inputPath, string outputPath)
        {
            string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string gltfpackExe = Path.Combine(assemblyDir, "gltfpack.exe");

            if (!File.Exists(gltfpackExe))
                throw new FileNotFoundException(
                    "gltfpack.exe was not found next to the plugin assembly. " +
                    "Make sure the EveryBIM.gltfpack.Binaries NuGet package is installed.",
                    gltfpackExe);

            var args = new StringBuilder();
            args.Append($"-i \"{inputPath}\" -o \"{outputPath}\"");

            // For Revit exports, we MUST keep named nodes to preserve the Element/Instance hierarchy
            args.Append(" -kn");
            // Also keep extras (metadata)
            args.Append(" -ke");

            if (_settings.UseMeshoptimizer)
                args.Append(" -c");           // EXT_meshopt_compression

            if (_settings.UseKtx2TextureCompression)
            {
                args.Append(" -tc");          // KHR_texture_basisu / KTX2
                args.Append($" -tq {_settings.TextureQuality}");
            }

            // Apply vertex precision or disable quantization entirely
            if (_settings.UseCustomVertexPrecision)
            {
                args.Append($" -vp {(int)_settings.VertexPositionPrecision}");
            }
            else
            {
                args.Append(" -noq"); // Disable quantization
            }

            // Apply simplification ratio if greater than 0
            if (_settings.SimplificationRatio > 0)
            {
                args.Append($" -si {_settings.SimplificationRatio:F2}");
            }

            var stderrBuilder = new StringBuilder();

            var psi = new ProcessStartInfo
            {
                FileName               = gltfpackExe,
                Arguments              = args.ToString(),
                UseShellExecute        = false,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };

            using (var process = Process.Start(psi))
            {
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        stderrBuilder.AppendLine(e.Data);
                };
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException(
                        $"gltfpack exited with code {process.ExitCode}.\n{stderrBuilder}");
            }
        }
    }
}
