using Dapper;
using Dapper.Contrib.Extensions;
using Microsoft.Data.Sqlite;
using RevitTrueGltf.Models;
using SharpGLTF.Scenes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace RevitTrueGltf.ExportStrategies
{
    /// <summary>
    /// Exports Revit parameters to an external SQLite database.
    /// 
    /// DATABASE RELATIONSHIPS:
    /// - Categories  (1 : N) Elements         : Each element belongs to one category.
    /// - Levels      (1 : N) Elements         : Each element references the Level it sits on (via LevelId).
    /// - Elements    (1 : N) ElementParameters: Junction table linking elements with their parameter values.
    /// - ParameterDefinitions (1 : N) ElementParameters: Shared metadata for parameters across elements.
    /// 
    /// Level elements are routed to the Levels table (detected via CategoryKey == "OST_Levels").
    /// All other elements are routed to the Elements table.
    /// </summary>
    public class SqliteExternalStrategy : IParameterExportStrategy
    {
        private SqliteConnection _conn;
        private SqliteTransaction _tx;

        // Caching for de-duplication of Categories and Definitions
        private readonly Dictionary<string, int> _categoryCache = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _definitionCache = new Dictionary<string, int>();

        private int _elemIdSeq = 1, _levelIdSeq = 1, _defIdSeq = 1, _catIdSeq = 1;

        public void Initialize(string exportFilePath, NodeBuilder rootNode)
        {
            string dbPath = Path.ChangeExtension(exportFilePath, ".sqlite");
            if (File.Exists(dbPath)) File.Delete(dbPath);

            try
            {
                _conn = new SqliteConnection($"Data Source={dbPath}");
                _conn.Open();
                CreateSchema(_conn);
                _tx = _conn.BeginTransaction();
            }
            catch (Exception)
            {
            }

            rootNode.Extras = new JsonObject { ["ExportTime"] = DateTime.Now.ToString("O") };
        }

        public void OnElement(ElementDataDTO elem, NodeBuilder elementNode)
        {
            if (elem == null) return;

            // Route Level elements to the dedicated Levels table
            if (elem.CategoryKey == "OST_Levels")
            {
                InsertLevel(elem, elementNode);
                return;
            }

            // 1. Insert Category (De-duplicated)
            int catDbId = 0;
            if (!string.IsNullOrEmpty(elem.CategoryKey))
            {
                if (!_categoryCache.TryGetValue(elem.CategoryKey, out catDbId))
                {
                    catDbId = _catIdSeq++;
                    _categoryCache[elem.CategoryKey] = catDbId;
                    _conn.Insert(new DbCategory { Id = catDbId, CategoryKey = elem.CategoryKey, CategoryName = elem.CategoryName ?? "" }, _tx);
                }
            }

            // 2. Insert Element
            int elemDbId = _elemIdSeq++;
            _conn.Insert(new DbElement
            {
                Id = elemDbId,
                UniqueId = elem.UniqueId,
                ElementId = elem.ElementId,
                CategoryId = catDbId > 0 ? catDbId : (int?)null,
                LevelUniqueId = elem.LevelUniqueId
            }, _tx);

            // 3. Process Parameters (De-duplicate definitions WITHIN the same element to prevent PK violation)
            var processedDefs = new HashSet<int>();
            foreach (var param in elem.Parameters)
            {
                // Definition de-duplication
                if (!_definitionCache.TryGetValue(param.UniqueKey, out int defDbId))
                {
                    defDbId = _defIdSeq++;
                    _definitionCache[param.UniqueKey] = defDbId;
                    _conn.Insert(new DbParameterDefinition
                    {
                        Id = defDbId,
                        UniqueKey = param.UniqueKey,
                        DisplayName = param.Name,
                        DataType = (int)param.DataType,
                        Source = (int)param.Source,
                        GroupKey = param.GroupKey ?? "",
                        GroupName = param.GroupName ?? "",
                        UnitType = param.UnitType ?? "None"
                    }, _tx);
                }

                // Check if we already added this definition for THIS element
                if (!processedDefs.Add(defDbId))
                {
                    // Skip duplicates within the same element's parameter list
                    continue;
                }

                // Directly insert Value into ElementParameters table
                _conn.Execute("INSERT OR REPLACE INTO ElementParameters (ElementId, DefinitionId, Value) VALUES (@ElementId, @DefinitionId, @Value)",
                    new { ElementId = elemDbId, DefinitionId = defDbId, Value = param.Value ?? "" }, _tx);
            }

            elementNode.Extras = new JsonObject { ["UniqueId"] = elem.UniqueId };
        }

        public void FinalizeExport()
        {
            _tx?.Commit();
            _tx?.Dispose();
            _conn?.Close();
            _conn?.Dispose();
        }

        /// <summary>
        /// Inserts a Level element into the dedicated Levels table.
        /// Elevation is read from the LEVEL_ELEV built-in parameter value.
        /// </summary>
        private void InsertLevel(ElementDataDTO elem, NodeBuilder levelNode)
        {
            // Find the Elevation parameter (BuiltInParameter.LEVEL_ELEV → UniqueKey "LEVEL_ELEV")
            var elevParam = elem.Parameters.FirstOrDefault(p => p.UniqueKey == "LEVEL_ELEV");

            _conn.Insert(new DbLevel
            {
                Id = _levelIdSeq++,
                UniqueId = elem.UniqueId,
                ElementId = elem.ElementId,
                Name = elem.Name ?? "",
                Elevation = elevParam?.Value ?? ""
            }, _tx);

            // Attach a lightweight extras to the glTF level node for front-end identification
            levelNode.Extras = new JsonObject
            {
                ["ElementId"] = elem.ElementId,
                ["UniqueId"] = elem.UniqueId
            };
        }

        private void CreateSchema(SqliteConnection conn)
        {
            conn.Execute(@"
                CREATE TABLE Categories (Id INTEGER PRIMARY KEY, CategoryKey TEXT NOT NULL UNIQUE, CategoryName TEXT NOT NULL);

                CREATE TABLE Levels (Id INTEGER PRIMARY KEY, UniqueId TEXT NOT NULL UNIQUE, ElementId INTEGER, Name TEXT NOT NULL, Elevation TEXT);
                CREATE INDEX IDX_Levels_UniqueId ON Levels(UniqueId);

                CREATE TABLE Elements (Id INTEGER PRIMARY KEY, UniqueId TEXT NOT NULL UNIQUE, ElementId INTEGER, CategoryId INTEGER,
                                     LevelUniqueId TEXT,
                                     FOREIGN KEY (CategoryId) REFERENCES Categories(Id),
                                     FOREIGN KEY (LevelUniqueId) REFERENCES Levels(UniqueId));
                CREATE INDEX IDX_Elements_UniqueId ON Elements(UniqueId);
                CREATE INDEX IDX_Elements_LevelUniqueId ON Elements(LevelUniqueId);

                CREATE TABLE ParameterDefinitions (Id INTEGER PRIMARY KEY, UniqueKey TEXT NOT NULL UNIQUE, DisplayName TEXT NOT NULL, 
                                                  DataType INTEGER NOT NULL, Source INTEGER NOT NULL, GroupKey TEXT NOT NULL, GroupName TEXT NOT NULL, UnitType TEXT);
                
                CREATE TABLE ElementParameters (ElementId INTEGER NOT NULL, DefinitionId INTEGER NOT NULL, Value TEXT, 
                                              PRIMARY KEY (ElementId, DefinitionId), 
                                              FOREIGN KEY (ElementId) REFERENCES Elements(Id), 
                                              FOREIGN KEY (DefinitionId) REFERENCES ParameterDefinitions(Id));
                CREATE INDEX IDX_ElemParams_ElemId ON ElementParameters(ElementId);
            ");
        }
    }
}
