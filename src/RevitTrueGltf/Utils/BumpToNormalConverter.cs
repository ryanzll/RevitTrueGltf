using SharpGLTF.Memory;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;
using System.Numerics;

namespace RevitTrueGltf.Utils
{
    /// <summary>
    /// Utility class specifically dedicated to converting Revit thickness/bump maps
    /// into tangent-space normal maps suitable for the glTF format.
    /// </summary>
    public static class BumpToNormalConverter
    {
        /// <summary>
        /// Reads a grayscale height map (bump map) and converts it into a tangent-space normal map
        /// stored directly as a SharpGLTF MemoryImage, avoiding intermediate external byte arrays.
        /// </summary>
        /// <param name="bumpPath">Path to the physical bump texture file.</param>
        /// <param name="strength">Strength of the normal effect.</param>
        /// <returns>A MemoryImage representing the generated PNG normal map.</returns>
        public static MemoryImage Convert(string bumpPath, float strength = 2.0f)
        {
            using (var img = Image.Load<Rgba32>(bumpPath))
            {
                int w = img.Width;
                int h = img.Height;
                using (var normalImg = new Image<Rgba32>(w, h))
                {
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int xLeft = Math.Max(0, x - 1);
                            int xRight = Math.Min(w - 1, x + 1);
                            int yUp = Math.Max(0, y - 1);
                            int yDown = Math.Min(h - 1, y + 1);

                            // Assuming the bump map is grayscale, read the R channel to represent height (0 to 1)
                            float left = img[xLeft, y].R / 255f;
                            float right = img[xRight, y].R / 255f;
                            float up = img[x, yUp].R / 255f;
                            float down = img[x, yDown].R / 255f;

                            // Finite difference calculation for gradients
                            float dx = (right - left) * strength;

                            // glTF normal map Y+ is UP. Image coordinates: Y+ is DOWN.
                            float dy = (up - down) * strength;

                            // Normal calculation: Z points outwards.
                            var n = Vector3.Normalize(new Vector3(-dx, dy, 1.0f));

                            // Remap [-1, 1] to [0, 255] for RGB storage
                            byte r = (byte)((n.X * 0.5f + 0.5f) * 255.0f);
                            byte g = (byte)((n.Y * 0.5f + 0.5f) * 255.0f);
                            byte b = (byte)((n.Z * 0.5f + 0.5f) * 255.0f);

                            normalImg[x, y] = new Rgba32(r, g, b, 255);
                        }
                    }

                    // Save the resulting normal map image to a memory stream as PNG
                    // Using an expandable MemoryStream allows zero-allocation buffer fetching
                    using (var ms = new MemoryStream())
                    {
                        normalImg.SaveAsPng(ms);

                        // Zero-Allocation Optimization: Try to get the underlying ArraySegment 
                        // representing the valid range of data directly without allocating a new copied byte[] Array.
                        if (ms.TryGetBuffer(out ArraySegment<byte> buffer))
                        {
                            return new MemoryImage(buffer);
                        }

                        // Fallback if TryGetBuffer fails (should dynamically never happen with a fresh MemoryStream)
                        return new MemoryImage(ms.ToArray());
                    }
                }
            }
        }
    }
}
