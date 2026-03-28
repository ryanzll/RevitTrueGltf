using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using Microsoft.Win32;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace RevitTrueGltf
{
    enum MateriLibType
    {
        Unknown = 0,
        Generic = 1,
        Prism = 2,
    }

    enum MaterialResolution
    {
        Unknown = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    class MaterialLib
    {
        public MateriLibType Type;
        public IList<KeyValuePair<MaterialResolution, string>> LibPaths = new List<KeyValuePair<MaterialResolution, string>>();
    }

    class MaterialUtils
    {
        private readonly Vector4 DefaultColor = new Vector4(0.8f, 0.8f, 0.8f, 1.0f); // DefaultColor

        private static IList<MaterialLib> _materialLibs = new List<MaterialLib>(); // MaterialLibs>

        private static string _revitVersion = "";

        public static void Init(Application app)
        {
            //should be 2020/2021...
            _revitVersion = app.VersionNumber;
            _materialLibs.Clear();

            var materialLib = ParseLibRegistry("SOFTWARE\\WOW6432Node\\Autodesk\\ADSKTextureLibraryNew");
            if (materialLib != null && materialLib.LibPaths.Count > 0)
            {
                materialLib.Type = MateriLibType.Generic;
                _materialLibs.Add(materialLib);
            }

            materialLib = ParseLibRegistry("SOFTWARE\\WOW6432Node\\Autodesk\\ADSKPrismTextureLibraryNew");
            if (materialLib != null && materialLib.LibPaths.Count > 0)
            {
                materialLib.Type = MateriLibType.Prism;
                _materialLibs.Add(materialLib);
            }
        }

        static MaterialLib ParseLibRegistry(string libRegistryPath)
        {
            var materialLib = new MaterialLib();
            var libKey = Registry.LocalMachine.OpenSubKey(libRegistryPath);
            if (libKey == null)
            {
                return null;
            }
            foreach (var resolutionName in libKey.GetSubKeyNames())
            {
                var resolutionKey = libKey.OpenSubKey(resolutionName);
                if (resolutionKey == null)
                {
                    continue;
                }

                var versionKey = resolutionKey.OpenSubKey(_revitVersion);
                if (versionKey == null)
                {
                    continue;
                }

                var libPath = versionKey.GetValue("LibraryPaths") as string;
                if (string.IsNullOrEmpty(libPath) || Directory.Exists(libPath) == false)
                {
                    continue;
                }
                materialLib.LibPaths.Add(new KeyValuePair<MaterialResolution, string>((MaterialResolution)int.Parse(resolutionName), libPath));
            }

            return materialLib;
        }

        public bool Convert(MaterialNode node, MaterialBuilder materialBuilder)
        {
            if (node == null)
            {
                return false;
            }

            var appearance = node.GetAppearance();
            if (appearance == null)
            {
                return false;
            }

            // for material similar to Glass
            if (appearance.Name == "GlazingSchema")
            {
                return ConvertGlazingMaterial(appearance, materialBuilder);
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
                    var abosoluteTexturePath = GetAbsoluteTexturePath(textureProperty.Value);
                    if (!string.IsNullOrEmpty(abosoluteTexturePath) && File.Exists(abosoluteTexturePath))
                    {
                        MemoryImage memoryImage = new MemoryImage(abosoluteTexturePath);
                        ImageBuilder imageBuilder = ImageBuilder.From(memoryImage, null);
                        materialBuilder.WithBaseColor(imageBuilder);
                        isTextureApplied = true;
                    }
                }

                if (isTextureApplied)
                {
                    color = RevitDiffuseColorToGltfBaseColor(color, transparency, diffuseFade);
                }
                else
                {
                    color = new Vector4(color.X, color.Y, color.Z, transparency);
                }
            }
            else
            {
                color = new Vector4(node.Color.Red / 255f, node.Color.Green / 255f, node.Color.Blue / 255f, transparency);
            }

            // Apply tint color to the final color (creates a multiplied tint effect)
            color = new Vector4(color.X * tintColor.X, color.Y * tintColor.Y, color.Z * tintColor.Z, color.W);

            materialBuilder.WithBaseColor(color);

            float? roughness = null;
            AssetProperty glossinessProp = appearance.FindByName("generic_glossiness") as AssetProperty;
            if (glossinessProp != null)
            {
                AssetPropertyDouble doubleProperty = glossinessProp as AssetPropertyDouble;
                if (doubleProperty != null)
                {
                    double glossiness = doubleProperty.Value;
                    roughness = 1.0f - (float)glossiness / 100.0f;
                }
            }
            else
            {
                AssetProperty roughnessProp = appearance.FindByName("roughness_standard");
                if (roughnessProp != null)
                {
                    var doubleProperty = roughnessProp as AssetPropertyDouble;
                    roughness = (float)doubleProperty.Value;
                }
            }

            AssetPropertyBoolean metalProp = appearance.FindByName("generic_is_metal") as AssetPropertyBoolean;
            if (metalProp != null)
            {
                materialBuilder.WithMetallicRoughness(metalProp.Value ? 1.0f : 0.0f, roughness);
            }
            else
            {
                AssetPropertyDouble metalValueProp = appearance.FindByName("metal_f0") as AssetPropertyDouble;
                if (metalValueProp != null)
                {
                    materialBuilder.WithMetallicRoughness((float)metalValueProp.Value, roughness);
                }
            }

            AssetProperty bumpProp = appearance.FindByName("generic_bump_map"); // ?? appearance.FindByName("surface_normal")) as AssetPropertyString
            if (bumpProp != null)
            {
                IList<string> bumpTexturePropertyNames = new List<string> { "unifiedbitmap_Bitmap" };
                var textureProperty = FindTextureProperty(bumpProp, bumpTexturePropertyNames) as AssetPropertyString;
                if (textureProperty != null)
                {
                    string absoluteTexturePath = GetAbsoluteTexturePath(textureProperty.Value);
                    if (!string.IsNullOrEmpty(absoluteTexturePath) && File.Exists(absoluteTexturePath))
                    {
                        MemoryImage memoryImage = new MemoryImage(absoluteTexturePath);
                        ImageBuilder imageBuilder = ImageBuilder.From(memoryImage);
                        materialBuilder.WithNormal(imageBuilder);
                    }
                }
            }
            return true;
        }

        public bool ConvertGlazingMaterial(Asset asset, MaterialBuilder materialBuilder)
        {
            double[] baseColor = new double[] { 1.0, 1.0, 1.0 }; // Default white glass
            AssetPropertyDoubleArray4d transColorProp = asset.FindByName("glazing_transmittance_color") as AssetPropertyDoubleArray4d;
            if (transColorProp != null)
            {
                var transColor = transColorProp.GetValueAsDoubles();
                baseColor[0] = transColor[0];
                baseColor[1] = transColor[1];
                baseColor[2] = transColor[2];
            }

            // Check if Tint is enabled
            AssetPropertyBoolean tintToggleProp = asset.FindByName("common_Tint_toggle") as AssetPropertyBoolean;
            if (tintToggleProp != null && tintToggleProp.Value)
            {
                AssetPropertyDoubleArray4d tintColorProp = asset.FindByName("common_Tint_color") as AssetPropertyDoubleArray4d;
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
            AssetPropertyDouble reflectanceProp = asset.FindByName("glazing_reflectance") as AssetPropertyDouble;
            if (reflectanceProp != null) reflectance = reflectanceProp.Value;

            // Get the number of glass panes (affects the sense of thickness)
            int levels = 1;
            AssetPropertyInteger levelsProp = asset.FindByName("glazing_no_levels") as AssetPropertyInteger;
            if (levelsProp != null) levels = levelsProp.Value;

            // Levels and Reflectance both affect transparency (Alpha): more panes and higher reflectance lead to a more solid appearance
            // Assuming a single pane's transmittance is 0.8, overlapping multiple panes gradually decreases transmittance
            double singlePaneTransmittance = 0.8;
            double overallTransmittance = Math.Pow(singlePaneTransmittance, levels);

            // Considering the energy carried away by reflected light, the reflectance also enhances the solidity of the base color representation during Alpha blending
            double alpha = 1.0 - (overallTransmittance * (1.0 - reflectance));
            alpha = Math.Max(0.0, Math.Min(1.0, alpha));

            Vector4 colorVector = new Vector4((float)baseColor[0], (float)baseColor[1], (float)baseColor[2], (float)alpha);

            // 3. Build glTF material
            materialBuilder
                .WithDoubleSide(true)
                // Switch to Metallic/Roughness workflow
                .WithMetallicRoughnessShader()
                // Set BaseColor - default to a certain transparency level as a fallback if Transmission is not supported
                .WithBaseColor(colorVector)
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

        private Vector4 GetColorVector(AssetProperty prop)
        {
            if (prop is AssetPropertyDoubleArray4d colorProp)
            {
                var color = colorProp.GetValueAsColor();
                return new Vector4(color.Red / 255.0f, color.Green / 255f, color.Blue / 255f, 1.0f);
            }
            return new Vector4(1f, 1f, 1f, 1f);
        }

        private AssetProperty FindTextureProperty(AssetProperty assetProperty, IList<string> textPropertyNames)
        {
            if (assetProperty.Type == AssetPropertyType.Asset)
            {
                var asset = assetProperty as Asset;
                return FindTextureProperty(asset, textPropertyNames);
            }
            else
            {
                for (int i = 0; i < assetProperty.NumberOfConnectedProperties; i++)
                {
                    var textureProperty = FindTextureProperty(assetProperty.GetConnectedProperty(i), textPropertyNames);
                    if (null != textureProperty)
                    {
                        return textureProperty;
                    }
                }
                return null;
            }
        }

        private AssetProperty FindTextureProperty(Asset asset, IList<string> textPropertyNames)
        {
            AssetProperty assetTypeProprty = asset.FindByName("assettype");
            if (assetTypeProprty == null || (assetTypeProprty as AssetPropertyString).Value != "texture")
            {
                return null;
            }

            foreach (var textPropertyName in textPropertyNames)
            {
                var textureProp = asset.FindByName(textPropertyName);
                if (textureProp != null)
                {
                    return textureProp;
                }
            }

            for (int i = 0; i < asset.Size; i++)
            {
                var textureProp = FindTextureProperty(asset[i], textPropertyNames);
                if (null != textureProp)
                {
                    return textureProp;
                }
            }
            return null;
        }

        private string GetAbsoluteTexturePath(string rawTexturePath)
        {
            //HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Autodesk\ADSKTextureLibraryNew
            if (_materialLibs.Count <= 0)
            {
                return null;
            }

            if (string.IsNullOrEmpty(rawTexturePath))
                return null;

            // 1. may contain multiple paths separated by '|'
            string[] rawPaths = rawTexturePath.Split('|');

            for (int index = 0; index < rawPaths.Length; index++)
            {
                var rawPath = rawPaths[index].Trim().Replace("/", "\\");
                if (Path.IsPathRooted(rawPath) && File.Exists(rawPath))
                {
                    return rawPath;
                }

                foreach (var materailLib in _materialLibs)
                {
                    if (materailLib.Type != MateriLibType.Prism)
                    {
                        continue;
                    }

                    foreach (var libPath in materailLib.LibPaths)
                    {
                        string absoluteFilePath = Path.Combine(libPath.Value, rawPath);
                        if (File.Exists(absoluteFilePath))
                        {
                            return absoluteFilePath;
                        }
                    }
                }
            }

            return null;
        }

        Vector4 RevitDiffuseColorToGltfBaseColor(Vector4 diffuseColor, float transparency, float diffuseFade)
        {
            if (diffuseFade >= 0.99f)
            {
                // Case A: should set texture
                Vector4 gltfBaseColorFactor = new Vector4(1.0f, 1.0f, 1.0f, transparency);
                // gltfMaterial.BaseColorTexture = diffuseTexture; // Assign texture
                return gltfBaseColorFactor;
            }
            else if (diffuseFade <= 0.01f)
            {
                // Case B: no texture
                Vector4 gltfBaseColorFactor = new Vector4(diffuseColor.X, diffuseColor.Y, diffuseColor.Z, transparency);
                return gltfBaseColorFactor;
            }
            else
            {
                // Case C: Mixed and should set texture
                // BaseColor = RevitColor * (1 - diffuseFade) + White(1.0) * diffuseFade
                Vector4 gltfBaseColorFactor = new Vector4();
                gltfBaseColorFactor.X = (diffuseColor.X * (1.0f - diffuseFade)) + (1.0f * diffuseFade); // R
                gltfBaseColorFactor.Y = (diffuseColor.Y * (1.0f - diffuseFade)) + (1.0f * diffuseFade); // G
                gltfBaseColorFactor.Z = (diffuseColor.Z * (1.0f - diffuseFade)) + (1.0f * diffuseFade); // B
                gltfBaseColorFactor.W = transparency; // A remains unchanged
                return gltfBaseColorFactor;
            }
        }
    }
}
