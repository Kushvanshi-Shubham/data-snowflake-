using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using SAP.Middleware.Connector;
using System.Configuration;
using Snowflake.Data.Client;

namespace RFC_READ_TABLE
{

    internal class Program
    {
        // SAP RFC_READ_TABLE WA buffer is 512 bytes. Stay under it with a safety margin.
        const int MAX_ROW_BYTES = 480;

        static void Main(string[] args)
        {
            string RFC_MESSAGE = string.Empty;
            string sfConnStr = ConfigurationManager.ConnectionStrings["Snowflake"].ConnectionString;

            string sapTableName = ConfigurationManager.AppSettings["SAPTableName"] ?? "";
            string snowflakeTableName = ConfigurationManager.AppSettings["SnowflakeTableName"] ?? sapTableName;
            string fieldListConfig = ConfigurationManager.AppSettings["FieldList"] ?? "";
            int rowCount = int.Parse(ConfigurationManager.AppSettings["RowCount"] ?? "0");
            int rowSkips = int.Parse(ConfigurationManager.AppSettings["RowSkips"] ?? "0");
            string delimiter = ConfigurationManager.AppSettings["Delimiter"] ?? "|";

            try
            {
                RfcConfigParameters rfcPar = RFCconfing.rfcConfigparameters();
                RfcDestination dest = RfcDestinationManager.GetDestination(rfcPar);
                RfcRepository rfcrep = dest.Repository;

                Console.WriteLine($"Connected to SAP. Reading table: {sapTableName} (master drop & insert)");

                // Optional explicit field filter; empty = all columns
                List<string> requestedFields = new List<string>();
                if (!string.IsNullOrWhiteSpace(fieldListConfig))
                    requestedFields = fieldListConfig.Split(',').Select(f => f.Trim()).ToList();

                // 1. Pull full field metadata via NO_DATA (no buffer limit on metadata-only call)
                var allFields = GetFieldMetadata(rfcrep, dest, sapTableName, requestedFields);
                if (allFields.Count == 0)
                    throw new Exception("RFC_READ_TABLE returned no field metadata");

                Console.WriteLine($"Table has {allFields.Count} columns to load");

                // 2. Resolve primary key from DD03L for stitching
                var pkFields = GetPrimaryKeyFields(rfcrep, dest, sapTableName);
                var allNames = new HashSet<string>(allFields.Select(f => f.Name));
                var pkInScope = pkFields.Where(p => allNames.Contains(p)).ToList();

                bool stitchByPk = pkInScope.Count > 0;
                if (stitchByPk)
                    Console.WriteLine($"Primary key for stitching: {string.Join(",", pkInScope)}");
                else
                    Console.WriteLine("No primary key found in scope — falling back to positional stitching");

                // 3. Split into chunks ≤ MAX_ROW_BYTES, with PK in every chunk
                var chunks = ChunkFields(allFields, pkInScope, MAX_ROW_BYTES);
                Console.WriteLine($"Reading in {chunks.Count} chunk(s)");

                // 4. Read each chunk, stitch by PK (or by row index if no PK)
                var byKey = new Dictionary<string, Dictionary<string, string>>();
                var byIdx = new List<Dictionary<string, string>>();

                for (int ci = 0; ci < chunks.Count; ci++)
                {
                    var chunkFields = chunks[ci];
                    var rows = ReadChunk(rfcrep, dest, sapTableName, chunkFields, delimiter, rowCount, rowSkips);
                    Console.WriteLine($"  Chunk {ci + 1}/{chunks.Count}: {rows.Count} rows ({chunkFields.Count} fields)");

                    if (stitchByPk)
                    {
                        foreach (var rowDict in rows)
                        {
                            string key = string.Join("\x01", pkInScope.Select(p => rowDict.ContainsKey(p) ? rowDict[p] : ""));
                            if (!byKey.TryGetValue(key, out var merged))
                            {
                                merged = new Dictionary<string, string>();
                                byKey[key] = merged;
                            }
                            foreach (var kv in rowDict)
                                merged[kv.Key] = kv.Value;
                        }
                    }
                    else
                    {
                        if (ci == 0)
                        {
                            byIdx = rows;
                        }
                        else
                        {
                            if (rows.Count != byIdx.Count)
                                throw new Exception($"Chunk row count mismatch ({rows.Count} vs {byIdx.Count}) and no PK to stitch — aborting");
                            for (int i = 0; i < rows.Count; i++)
                                foreach (var kv in rows[i])
                                    byIdx[i][kv.Key] = kv.Value;
                        }
                    }
                }

                int totalStitchedRows = stitchByPk ? byKey.Count : byIdx.Count;
                if (totalStitchedRows == 0)
                {
                    Console.WriteLine("No data returned from SAP, nothing to load.");
                    RFC_MESSAGE = "No data";
                    return;
                }
                Console.WriteLine($"Total rows after stitching: {totalStitchedRows}");

                // 5. Build DataTable preserving original SAP column order
                DataTable dt = new DataTable(snowflakeTableName);
                foreach (var col in allFields)
                    dt.Columns.Add(col.Name, typeof(string));
                dt.Columns.Add("EXTRACT_DATE", typeof(DateTime)).DefaultValue = DateTime.Now.ToString("yyyy-MM-dd");

                IEnumerable<Dictionary<string, string>> stitchedRows = stitchByPk ? byKey.Values : (IEnumerable<Dictionary<string, string>>)byIdx;
                foreach (var rowKv in stitchedRows)
                {
                    DataRow row = dt.NewRow();
                    foreach (var col in allFields)
                        row[col.Name] = rowKv.ContainsKey(col.Name) ? rowKv[col.Name] : "";
                    dt.Rows.Add(row);
                }

                // 6. CSV
                string colNames = string.Join(", ", dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
                string colPositions = string.Join(", ", Enumerable.Range(1, dt.Columns.Count).Select(i => $"${i}"));
                string tempDir = Path.GetTempPath();
                string csvFileName = $"{snowflakeTableName}.csv";
                string csvFilePath = Path.Combine(tempDir, csvFileName);

                using (StreamWriter sw = new StreamWriter(csvFilePath, false, Encoding.UTF8))
                {
                    foreach (DataRow dataRow in dt.Rows)
                    {
                        var csvFields = new List<string>();
                        foreach (DataColumn col in dt.Columns)
                        {
                            object val = dataRow[col];
                            if (val == null || val == DBNull.Value)
                                csvFields.Add("");
                            else if (val is string strVal)
                                csvFields.Add($"\"{strVal.Replace("\"", "\"\"")}\"");
                            else if (val is DateTime dtVal)
                                csvFields.Add($"\"{dtVal:yyyy-MM-dd}\"");
                            else if (val is decimal || val is double || val is float)
                                csvFields.Add(Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture));
                            else
                                csvFields.Add(val.ToString());
                        }
                        sw.WriteLine(string.Join(",", csvFields));
                    }
                }

                long totalRows = dt.Rows.Count;
                dt.Dispose();

                // 7. Drop + Create + Load to Snowflake
                using (SnowflakeDbConnection sfCon = new SnowflakeDbConnection())
                {
                    sfCon.ConnectionString = sfConnStr;
                    sfCon.Open();

                    using (IDbCommand dropCmd = sfCon.CreateCommand())
                    {
                        dropCmd.CommandText = $"DROP TABLE IF EXISTS {snowflakeTableName}";
                        dropCmd.ExecuteNonQuery();
                        Console.WriteLine($"Dropped table {snowflakeTableName} (if existed)");
                    }

                    StringBuilder createSql = new StringBuilder();
                    createSql.Append($"CREATE TABLE {snowflakeTableName} (");
                    foreach (var col in allFields)
                    {
                        createSql.Append($"{col.Name} VARCHAR({Math.Max(col.Length, 1)})");
                        createSql.Append(", ");
                    }
                    createSql.Append("EXTRACT_DATE DATE)");

                    using (IDbCommand createCmd = sfCon.CreateCommand())
                    {
                        createCmd.CommandText = createSql.ToString();
                        createCmd.ExecuteNonQuery();
                    }
                    Console.WriteLine($"Created table {snowflakeTableName} in Snowflake");

                    string putPath = csvFilePath.Replace("\\", "/");
                    using (IDbCommand putCmd = sfCon.CreateCommand())
                    {
                        putCmd.CommandText = $"PUT 'file://{putPath}' @%{snowflakeTableName} AUTO_COMPRESS=TRUE OVERWRITE=TRUE";
                        putCmd.ExecuteNonQuery();
                    }

                    using (IDbCommand copyCmd = sfCon.CreateCommand())
                    {
                        copyCmd.CommandText =
                            $"COPY INTO {snowflakeTableName} ({colNames}) " +
                            $"FROM (SELECT {colPositions} FROM @%{snowflakeTableName}/{csvFileName}.gz) " +
                            "FILE_FORMAT=(TYPE='CSV' FIELD_OPTIONALLY_ENCLOSED_BY='\"' " +
                            "NULL_IF=('') EMPTY_FIELD_AS_NULL=TRUE) PURGE=TRUE";
                        copyCmd.ExecuteNonQuery();
                    }

                    sfCon.Close();
                }

                Console.WriteLine($"Loaded {totalRows} rows into Snowflake");

                if (File.Exists(csvFilePath))
                    File.Delete(csvFilePath);

                RFC_MESSAGE = "Successfully Inserted";
                Console.WriteLine($"\n{RFC_MESSAGE}");
            }
            catch (Exception ex)
            {
                RFC_MESSAGE = ex.Message;
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        // Metadata-only call. NO_DATA='X' returns FIELDS info without populating DATA, bypassing the 512-byte limit.
        static List<(string Name, int Offset, int Length)> GetFieldMetadata(
            RfcRepository rfcrep, RfcDestination dest, string tableName, List<string> requestedFields)
        {
            IRfcFunction f = rfcrep.CreateFunction("RFC_READ_TABLE");
            f.SetValue("QUERY_TABLE", tableName);
            f.SetValue("NO_DATA", "X");

            IRfcTable fieldsTable = f.GetTable("FIELDS");
            foreach (var fld in requestedFields)
            {
                fieldsTable.Append();
                fieldsTable.SetValue("FIELDNAME", fld);
            }

            f.Invoke(dest);

            IRfcTable retFields = f.GetTable("FIELDS");
            var meta = new List<(string Name, int Offset, int Length)>();
            for (int i = 0; i < retFields.RowCount; i++)
            {
                retFields.CurrentIndex = i;
                meta.Add((
                    retFields.GetString("FIELDNAME").Trim(),
                    retFields.GetInt("OFFSET"),
                    retFields.GetInt("LENGTH")
                ));
            }
            return meta;
        }

        // Look up key fields in DD03L (SAP data dictionary), ordered by POSITION.
        static List<string> GetPrimaryKeyFields(RfcRepository rfcrep, RfcDestination dest, string tableName)
        {
            try
            {
                var rows = ReadChunk(
                    rfcrep, dest, "DD03L",
                    new List<string> { "FIELDNAME", "POSITION", "KEYFLAG" },
                    "|", 0, 0,
                    new[] { $"TABNAME = '{tableName.ToUpper()}' AND KEYFLAG = 'X'" });

                var pks = new List<(string Name, int Pos)>();
                foreach (var row in rows)
                {
                    string fname = row.ContainsKey("FIELDNAME") ? row["FIELDNAME"] : "";
                    string posStr = row.ContainsKey("POSITION") ? row["POSITION"] : "0";
                    if (string.IsNullOrEmpty(fname)) continue;
                    int.TryParse(posStr, out int p);
                    pks.Add((fname, p));
                }
                return pks.OrderBy(x => x.Pos).Select(x => x.Name).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not read DD03L for primary key: {ex.Message}");
                return new List<string>();
            }
        }

        // Split fields so each chunk fits the WA buffer; PK fields are repeated in every chunk for stitching.
        static List<List<string>> ChunkFields(
            List<(string Name, int Offset, int Length)> fields,
            List<string> pkFields,
            int maxBytes)
        {
            var pkSet = new HashSet<string>(pkFields);
            var pkMeta = fields.Where(f => pkSet.Contains(f.Name)).ToList();
            int pkBytes = pkMeta.Sum(f => f.Length) + Math.Max(0, pkMeta.Count); // include delimiters
            int budget = maxBytes - pkBytes;
            if (budget <= 0)
                throw new Exception($"Primary key fields alone ({pkBytes} bytes) exceed row width budget ({maxBytes})");

            var nonPk = fields.Where(f => !pkSet.Contains(f.Name)).ToList();
            var chunks = new List<List<string>>();
            var current = new List<string>(pkFields);
            int currentNonPkBytes = 0;

            foreach (var f in nonPk)
            {
                int cost = f.Length + 1; // +1 for delimiter
                if (cost > budget)
                    throw new Exception($"Single field {f.Name} ({f.Length} bytes) exceeds row width budget ({budget} after PK)");

                if (currentNonPkBytes + cost > budget && current.Count > pkFields.Count)
                {
                    chunks.Add(current);
                    current = new List<string>(pkFields);
                    currentNonPkBytes = 0;
                }
                current.Add(f.Name);
                currentNonPkBytes += cost;
            }
            if (current.Count > 0) chunks.Add(current);
            return chunks;
        }

        // Read one chunk: returns a list of rows as field->value dictionaries.
        static List<Dictionary<string, string>> ReadChunk(
            RfcRepository rfcrep, RfcDestination dest, string tableName,
            List<string> chunkFields, string delimiter, int rowCount, int rowSkips,
            IEnumerable<string> options = null)
        {
            IRfcFunction f = rfcrep.CreateFunction("RFC_READ_TABLE");
            f.SetValue("QUERY_TABLE", tableName);
            f.SetValue("DELIMITER", delimiter);
            if (rowCount > 0) f.SetValue("ROWCOUNT", rowCount);
            if (rowSkips > 0) f.SetValue("ROWSKIPS", rowSkips);

            IRfcTable fieldsTable = f.GetTable("FIELDS");
            foreach (var fld in chunkFields)
            {
                fieldsTable.Append();
                fieldsTable.SetValue("FIELDNAME", fld);
            }

            if (options != null)
            {
                IRfcTable optTable = f.GetTable("OPTIONS");
                foreach (var clause in options)
                {
                    optTable.Append();
                    optTable.SetValue("TEXT", clause);
                }
            }

            f.Invoke(dest);

            IRfcTable retFields = f.GetTable("FIELDS");
            var colMeta = new List<(string Name, int Offset, int Length)>();
            for (int i = 0; i < retFields.RowCount; i++)
            {
                retFields.CurrentIndex = i;
                colMeta.Add((
                    retFields.GetString("FIELDNAME").Trim(),
                    retFields.GetInt("OFFSET"),
                    retFields.GetInt("LENGTH")
                ));
            }

            IRfcTable dataTable = f.GetTable("DATA");
            var rows = new List<Dictionary<string, string>>();
            for (int i = 0; i < dataTable.RowCount; i++)
            {
                dataTable.CurrentIndex = i;
                string wa = dataTable.GetString("WA");
                var rowDict = new Dictionary<string, string>();
                foreach (var col in colMeta)
                {
                    int off = col.Offset;
                    int len = col.Length;
                    if (off + len > wa.Length) len = Math.Max(0, wa.Length - off);
                    rowDict[col.Name] = off < wa.Length ? wa.Substring(off, len).Trim() : "";
                }
                rows.Add(rowDict);
            }
            return rows;
        }
    }
}
