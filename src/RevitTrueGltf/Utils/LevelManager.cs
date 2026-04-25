using Autodesk.Revit.DB;
using RevitTrueGltf.Models;
using System.Collections.Generic;
using System.Linq;

namespace RevitTrueGltf.Utils
{
    /// <summary>
    /// Internal manager for all Level-related Revit logic.
    /// Responsibilities:
    ///   1. Collect and cache all Levels from the document, sorted by Elevation.
    ///   2. Extract Level DTOs (treated as regular elements) for glTF node creation.
    ///   3. Attach level info to any element DTO using a three-step inference fallback.
    ///
    /// Dependency rule: This class ONLY references Revit API types and RevitTrueGltf.Models.
    /// It has ZERO knowledge of glTF (NodeBuilder, IParameterExportStrategy, etc.).
    /// </summary>
    internal static class LevelManager
    {
        // Cached levels sorted by Elevation ascending. Populated in Initialize().
        private static List<Level> _cachedLevels = new List<Level>();

        // Tolerance in feet applied downward when comparing BoundingBox Min.Z to level Elevation.
        // A small value (~0.1 ft ≈ 3 cm) handles elements whose bottom face sits exactly on the slab
        // surface or slightly below due to modeling conventions.
        private const double ToleranceFeet = 0.1;

        /// <summary>
        /// Collects all levels in the document, caches them sorted by Elevation,
        /// and returns a list of (Level element, ElementDataDTO) pairs for the caller
        /// to use when creating glTF Level nodes.
        /// </summary>
        public static List<(Level Level, ElementDataDTO Dto)> Initialize(Document document)
        {
            _cachedLevels.Clear();

            var levels = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            _cachedLevels.AddRange(levels);

            // Build DTOs for each level so ExportGltfContext can create glTF nodes.
            // The level is treated as a regular element — its Revit parameters (e.g. Elevation)
            // are extracted by RevitParameterExtractor just like any other element.
            var result = new List<(Level, ElementDataDTO)>();
            foreach (var level in _cachedLevels)
            {
                var dto = RevitParameterExtractor.ExtractParameters(document, level);
                result.Add((level, dto));
            }

            return result;
        }

        /// <summary>
        /// Attaches LevelName and LevelId to the given DTO using a three-step fallback:
        ///   1. element.LevelId
        ///   2. Common BuiltIn level parameters
        ///   3. BoundingBox Min.Z geometric inference against the cached level list
        /// If no level is resolved, DTO retains its default values ("None" / -1).
        /// </summary>
        public static void AttachLevelInfo(Document document, Element element, ElementDataDTO dto)
        {
            // Level elements themselves are the reference datum — they don't belong to a level.
            // This guard also covers the Initialize() path where ExtractParameters is called for Level objects.
            if (element is Level) return;

            Level level = null;

            // Step 1: Direct LevelId property
            if (element.LevelId != null && element.LevelId != ElementId.InvalidElementId)
            {
                level = document.GetElement(element.LevelId) as Level;
            }

            // Step 2: BuiltIn parameter fallback
            if (level == null)
            {
                var candidates = new[]
                {
                    BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
                    BuiltInParameter.FAMILY_LEVEL_PARAM,
                    BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
                    BuiltInParameter.FACEROOF_LEVEL_PARAM,
                    BuiltInParameter.ROOF_BASE_LEVEL_PARAM
                };

                foreach (var bip in candidates)
                {
                    var param = element.get_Parameter(bip);
                    if (param != null && param.HasValue)
                    {
                        var id = param.AsElementId();
                        if (id != null && id != ElementId.InvalidElementId)
                        {
                            level = document.GetElement(id) as Level;
                            if (level != null) break;
                        }
                    }
                }
            }

            // Step 3: BoundingBox geometric inference
            if (level == null && _cachedLevels.Count > 0)
            {
                var bbox = element.get_BoundingBox(null);
                if (bbox != null)
                {
                    double adjustedMinZ = bbox.Min.Z + ToleranceFeet;

                    // Iterate descending: find the highest level whose Elevation is <= adjustedMinZ
                    for (int i = _cachedLevels.Count - 1; i >= 0; i--)
                    {
                        if (_cachedLevels[i].Elevation <= adjustedMinZ)
                        {
                            level = _cachedLevels[i];
                            break;
                        }
                    }

                    // Element is below the lowest level — assign it to the lowest level anyway
                    if (level == null)
                    {
                        level = _cachedLevels[0];
                    }
                }
            }

            // Apply to DTO (if still null, DTO defaults stay: "None" / "")
            if (level != null)
            {
                dto.LevelName = level.Name;
                dto.LevelUniqueId = level.UniqueId;
            }
        }

        /// <summary>
        /// Clears the internal level cache. Should be called in ExportGltfContext.Finish().
        /// </summary>
        public static void Clear()
        {
            _cachedLevels.Clear();
        }
    }
}
