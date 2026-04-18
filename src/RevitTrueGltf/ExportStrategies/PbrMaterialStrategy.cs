using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Microsoft.Win32;
using RevitTrueGltf.Utils;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace RevitTrueGltf.ExportStrategies
{
    // Support types — internal to this file, not part of the public API
    internal enum MaterialLibType { Unknown = 0, Generic = 1, Prism = 2 }
    internal enum MaterialLibResolution { Unknown = 0, Low = 1, Medium = 2, High = 3 }
    internal class MaterialLib
    {
        public MaterialLibType Type;
        public IList<KeyValuePair<MaterialLibResolution, string>> LibPaths
            = new List<KeyValuePair<MaterialLibResolution, string>>();
    }

    /// <summary>
    /// Full PBR material strategy. Parses Revit appearance assets to extract textures,
    /// roughness, metalness, and normal maps, producing a PBR-compatible glTF material.
    /// Falls back to <see cref="ColorOnlyMaterialStrategy"/> when the appearance asset
    /// cannot be recognized (missing, unsupported schema, or texture not found on disk).
    ///
    /// This class absorbs all logic previously contained in MaterialUtils. Texture library
    /// paths are resolved from the Windows registry once at first instantiation, using
    /// <see cref="RevitContext.VersionNumber"/> which must be set before construction.
    /// </summary>
    public class PbrMaterialStrategy : IMaterialStrategy
    {
        private static readonly Vector4 DefaultColor = new Vector4(0.8f, 0.8f, 0.8f, 1.0f); // DefaultColor

        // Lazily initialized static cache — populated once across the entire plugin session.
        // Safe because RevitContext.VersionNumber is always set before the first instantiation.
        private static IList<MaterialLib> _materialLibs; // MaterialLibs>

        private readonly IMaterialStrategy _colorFallback = new ColorOnlyMaterialStrategy();

        public PbrMaterialStrategy()
        {
            if (_materialLibs == null)
            {
                _materialLibs = BuildMaterialLibs();
            }
        }

        // ── IMaterialStrategy ──────────────────────────────────────────────────────────

        public MaterialBuilder Build(MaterialNode node)
        {
            if (node == null)
                return _colorFallback.Build(node);

            var appearance = node.GetAppearance();
            if (appearance == null)
                return _colorFallback.Build(node);

            var builder = new MaterialBuilder(node.NodeName);

            // for material similar to Glass
            if (appearance.Name == "GlazingSchema")
            {
                return BuildGlazingMaterial(appearance, builder) ? builder : _colorFallback.Build(node);
            }

            var diffuseFadeProperty = appearance.FindByName("generic_diffuse_image_fade");
            float diffuseFade = 1.0f;
            if (diffuseFadeProperty != null)
            {
                var doubleProperty = diffuseFadeProperty as AssetPropertyDouble;
                diffuseFade = (float)doubleProperty.Value;
            }

            var transparencyProperty = appearance.FindByName("generic_transparency");
            float transparency = 1.0f;
            if (transparencyProperty != null)
            {
                var doubleProperty = transparencyProperty as AssetPropertyDouble;
                transparency = (float)doubleProperty.Value;
            }

            var tintColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
            var tintToggleProp = appearance.FindByName("common_Tint_toggle") as AssetPropertyBoolean;
            if (tintToggleProp == null || tintToggleProp.Value) // If no toggle, assume true so we try to get tint color
            {
                var tintProperty = appearance.FindByName("common_Tint_color");
                if (tintProperty != null)
                {
                    tintColor = GetColorVector(tintProperty);
                }
            }

            Vector4 color = DefaultColor;
            var colorProperty = appearance.FindByName("generic_diffuse") ?? appearance.FindByName("opaque_albedo");
            if (colorProperty != null)
            {
                color = GetColorVector(colorProperty);

                // https://jeremytammik.github.io/tbc/a/1596_texture_path.html
                // var test = asset[UnifiedBitmap.UnifiedbitmapBitmap];
                // This line is 2018.1 & up because of the 
                // property reference to UnifiedBitmap
                // .UnifiedbitmapBitmap.  In earlier versions,
                // you can still reference the string name 
                // instead: "unifiedbitmap_Bitmap"
                IList<string> texturePropertyNames = new List<string> { "unifiedbitmap_Bitmap", "UnifiedBitmapSchema" };
                var textureProperty = FindTextureProperty(colorProperty, texturePropertyNames) as AssetPropertyString;
                bool isTextureApplied = false;
                if (textureProperty != null)
                {
                    var absoluteTexturePath = GetAbsoluteTexturePath(textureProperty.Value);
                    if (!string.IsNullOrEmpty(absoluteTexturePath) && File.Exists(absoluteTexturePath))
                    {
                        MemoryImage memoryImage = new MemoryImage(absoluteTexturePath);
                        ImageBuilder imageBuilder = ImageBuilder.From(memoryImage, null);
                        builder.WithBaseColor(imageBuilder);
                        isTextureApplied = true;
                    }
                }

                color = isTextureApplied
                    ? RevitDiffuseColorToGltfBaseColor(color, transparency, diffuseFade)
                    : new Vector4(color.X, color.Y, color.Z, transparency);
            }
            else
            {
                color = new Vector4(node.Color.Red / 255f, node.Color.Green / 255f, node.Color.Blue / 255f, transparency);
            }

            // Apply tint color to the final color (creates a multiplied tint effect)
            color = new Vector4(color.X * tintColor.X, color.Y * tintColor.Y, color.Z * tintColor.Z, color.W);
            builder.WithBaseColor(color);

            // Roughness
            float? roughness = null;
            var glossinessProp = appearance.FindByName("generic_glossiness") as AssetPropertyDouble;
            if (glossinessProp != null)
            {
                roughness = 1.0f - (float)glossinessProp.Value / 100.0f;
            }
            else
            {
                var roughnessProp = appearance.FindByName("roughness_standard") as AssetPropertyDouble;
                if (roughnessProp != null)
                    roughness = (float)roughnessProp.Value;
            }

            // Metalness
            var metalProp = appearance.FindByName("generic_is_metal") as AssetPropertyBoolean;
            if (metalProp != null)
            {
                builder.WithMetallicRoughness(metalProp.Value ? 1.0f : 0.0f, roughness);
            }
            else
            {
                var metalValueProp = appearance.FindByName("metal_f0") as AssetPropertyDouble;
                if (metalValueProp != null)
                    builder.WithMetallicRoughness((float)metalValueProp.Value, roughness);
            }

            var bumpProp = appearance.FindByName("generic_bump_map"); // ?? appearance.FindByName("surface_normal")) as AssetPropertyString
            if (bumpProp != null)
            {
                IList<string> bumpTexturePropertyNames = new List<string> { "unifiedbitmap_Bitmap" };
                var textureProperty = FindTextureProperty(bumpProp, bumpTexturePropertyNames) as AssetPropertyString;
                if (textureProperty != null)
                {
                    string absoluteTexturePath = GetAbsoluteTexturePath(textureProperty.Value);
                    if (!string.IsNullOrEmpty(absoluteTexturePath) && File.Exists(absoluteTexturePath))
                    {
                        // Convert the height/bump map to a proper tangent-space normal map and get it as a MemoryImage
                        MemoryImage memoryImage = BumpToNormalConverter.Convert(absoluteTexturePath);
                        ImageBuilder imageBuilder = ImageBuilder.From(memoryImage);
                        builder.WithNormal(imageBuilder);
                    }
                }
            }

            return builder;
        }

        // ── Glazing ────────────────────────────────────────────────────────────────────

        private bool BuildGlazingMaterial(Asset asset, MaterialBuilder materialBuilder)
        {
            double[] baseColor = new double[] { 1.0, 1.0, 1.0 }; // Default white glass
            var transColorProp = asset.FindByName("glazing_transmittance_color") as AssetPropertyDoubleArray4d;
            if (transColorProp != null)
            {
                var transColor = transColorProp.GetValueAsDoubles();
                baseColor[0] = transColor[0];
                baseColor[1] = transColor[1];
                baseColor[2] = transColor[2];
            }

            // Check if Tint is enabled
            var tintToggleProp = asset.FindByName("common_Tint_toggle") as AssetPropertyBoolean;
            if (tintToggleProp != null && tintToggleProp.Value)
            {
                var tintColorProp = asset.FindByName("common_Tint_color") as AssetPropertyDoubleArray4d;
                if (tintColorProp != null)
                {
                    // If Tint is enabled, the tint color usually overrides the transmittance color
                    var tintColor = tintColorProp.GetValueAsDoubles();
                    baseColor[0] = tintColor[0];
                    baseColor[1] = tintColor[1];
                    baseColor[2] = tintColor[2];
                }
            }

            // Get reflectance
            double reflectance = 0.1;
            var reflectanceProp = asset.FindByName("glazing_reflectance") as AssetPropertyDouble;
            if (reflectanceProp != null) reflectance = reflectanceProp.Value;

            // Get the number of glass panes (affects the sense of thickness)
            int levels = 1;
            var levelsProp = asset.FindByName("glazing_no_levels") as AssetPropertyInteger;
            if (levelsProp != null) levels = levelsProp.Value;

            // Levels and Reflectance both affect transparency (Alpha): more panes and higher reflectance lead to a more solid appearance
            // Assuming a single pane's transmittance is 0.8, overlapping multiple panes gradually decreases transmittance
            double singlePaneTransmittance = 0.8;
            double overallTransmittance = Math.Pow(singlePaneTransmittance, levels);
            // Considering the energy carried away by reflected light, the reflectance also enhances the solidity of the base color representation during Alpha blending
            double alpha = 1.0 - overallTransmittance * (1.0 - reflectance);
            alpha = Math.Max(0.0, Math.Min(1.0, alpha));

            // 3. Build glTF material
            materialBuilder
                .WithDoubleSide(true)
                // Switch to Metallic/Roughness workflow
                .WithMetallicRoughnessShader()
                // Set BaseColor - default to a certain transparency level as a fallback if Transmission is not supported
                .WithBaseColor(new Vector4((float)baseColor[0], (float)baseColor[1], (float)baseColor[2], (float)alpha))
                .WithAlpha(AlphaMode.BLEND)
                // Dielectric/glass
                .WithMetallicRoughness(0.0f, 0.05f);

            try
            {
                // Set physical transmission (Transmission extension)
                materialBuilder.WithTransmission(null, (float)overallTransmittance);
            }
            catch { }

            return true;
        }

        // ── Texture Library Initialization ─────────────────────────────────────────────

        private static IList<MaterialLib> BuildMaterialLibs()
        {
            var libs = new List<MaterialLib>();

            //HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Autodesk\ADSKTextureLibraryNew
            var lib = ParseLibRegistry("SOFTWARE\\WOW6432Node\\Autodesk\\ADSKTextureLibraryNew");
            if (lib != null && lib.LibPaths.Count > 0)
            {
                lib.Type = MaterialLibType.Generic;
                libs.Add(lib);
            }

            lib = ParseLibRegistry("SOFTWARE\\WOW6432Node\\Autodesk\\ADSKPrismTextureLibraryNew");
            if (lib != null && lib.LibPaths.Count > 0)
            {
                lib.Type = MaterialLibType.Prism;
                libs.Add(lib);
            }

            return libs;
        }

        private static MaterialLib ParseLibRegistry(string libRegistryPath)
        {
            var materialLib = new MaterialLib();
            var libKey = Registry.LocalMachine.OpenSubKey(libRegistryPath);
            if (libKey == null) return null;

            foreach (var resolutionName in libKey.GetSubKeyNames())
            {
                var resolutionKey = libKey.OpenSubKey(resolutionName);
                if (resolutionKey == null) continue;

                // Use RevitContext.VersionNumber (set by ExportGltfCommand before first instantiation)
                var versionKey = resolutionKey.OpenSubKey(RevitContext.VersionNumber);
                if (versionKey == null) continue;

                var libPath = versionKey.GetValue("LibraryPaths") as string;
                if (string.IsNullOrEmpty(libPath) || !Directory.Exists(libPath)) continue;

                materialLib.LibPaths.Add(new KeyValuePair<MaterialLibResolution, string>(
                    (MaterialLibResolution)int.Parse(resolutionName), libPath));
            }

            return materialLib;
        }

        // ── Private Helpers ────────────────────────────────────────────────────────────

        private Vector4 GetColorVector(AssetProperty prop)
        {
            if (prop is AssetPropertyDoubleArray4d colorProp)
            {
                var color = colorProp.GetValueAsColor();
                return new Vector4(color.Red / 255.0f, color.Green / 255f, color.Blue / 255f, 1.0f);
            }
            return new Vector4(1f, 1f, 1f, 1f);
        }

        private AssetProperty FindTextureProperty(AssetProperty assetProperty, IList<string> texturePropertyNames)
        {
            if (assetProperty.Type == AssetPropertyType.Asset)
            {
                var asset = assetProperty as Asset;
                return FindTextureProperty(asset, texturePropertyNames);
            }
            else
            {
                for (int i = 0; i < assetProperty.NumberOfConnectedProperties; i++)
                {
                    var textureProperty = FindTextureProperty(assetProperty.GetConnectedProperty(i), texturePropertyNames);
                    if (textureProperty != null) return textureProperty;
                }
                return null;
            }
        }

        private AssetProperty FindTextureProperty(Asset asset, IList<string> texturePropertyNames)
        {
            var assetTypeProp = asset.FindByName("assettype");
            if (assetTypeProp == null || (assetTypeProp as AssetPropertyString).Value != "texture")
                return null;

            foreach (var name in texturePropertyNames)
            {
                var textureProp = asset.FindByName(name);
                if (textureProp != null) return textureProp;
            }

            for (int i = 0; i < asset.Size; i++)
            {
                var textureProp = FindTextureProperty(asset[i], texturePropertyNames);
                if (textureProp != null) return textureProp;
            }

            return null;
        }

        private string GetAbsoluteTexturePath(string rawTexturePath)
        {
            if (_materialLibs == null || _materialLibs.Count <= 0) return null;
            if (string.IsNullOrEmpty(rawTexturePath)) return null;

            // 1. may contain multiple paths separated by '|'
            string[] rawPaths = rawTexturePath.Split('|');
            for (int index = 0; index < rawPaths.Length; index++)
            {
                var rawPath = rawPaths[index].Trim().Replace("/", "\\");
                if (Path.IsPathRooted(rawPath) && File.Exists(rawPath))
                    return rawPath;

                foreach (var materialLib in _materialLibs)
                {
                    if (materialLib.Type != MaterialLibType.Prism) continue;
                    foreach (var libPath in materialLib.LibPaths)
                    {
                        string absoluteFilePath = Path.Combine(libPath.Value, rawPath);
                        if (File.Exists(absoluteFilePath)) return absoluteFilePath;
                    }
                }
            }

            return null;
        }

        private Vector4 RevitDiffuseColorToGltfBaseColor(Vector4 diffuseColor, float transparency, float diffuseFade)
        {
            if (diffuseFade >= 0.99f)
            {
                // Case A: should set texture
                // gltfMaterial.BaseColorTexture = diffuseTexture; // Assign texture
                return new Vector4(1.0f, 1.0f, 1.0f, transparency);
            }
            else if (diffuseFade <= 0.01f)
            {
                // Case B: no texture
                return new Vector4(diffuseColor.X, diffuseColor.Y, diffuseColor.Z, transparency);
            }
            else
            {
                // Case C: Mixed and should set texture
                // BaseColor = RevitColor * (1 - diffuseFade) + White(1.0) * diffuseFade
                return new Vector4(
                    diffuseColor.X * (1.0f - diffuseFade) + 1.0f * diffuseFade, // R
                    diffuseColor.Y * (1.0f - diffuseFade) + 1.0f * diffuseFade, // G
                    diffuseColor.Z * (1.0f - diffuseFade) + 1.0f * diffuseFade, // B
                    transparency // A remains unchanged
                );
            }
        }
    }
}
