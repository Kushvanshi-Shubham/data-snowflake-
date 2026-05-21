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
        static void Main(string[] args)
        {
            string RFC_MESSAGE = string.Empty;
            string sfConnStr = ConfigurationManager.ConnectionStrings["Snowflake"].ConnectionString;

            // Read SAP table configuration from App.config
            string sapTableName = ConfigurationManager.AppSettings["SAPTableName"] ?? "";
            string snowflakeTableName = ConfigurationManager.AppSettings["SnowflakeTableName"] ?? sapTableName;
            string fieldListConfig = ConfigurationManager.AppSettings["FieldList"] ?? "";
            string dateField = ConfigurationManager.AppSettings["DateField"] ?? "";
            int rowCount = int.Parse(ConfigurationManager.AppSettings["RowCount"] ?? "0");
            int rowSkips = int.Parse(ConfigurationManager.AppSettings["RowSkips"] ?? "0");
            string delimiter = ConfigurationManager.AppSettings["Delimiter"] ?? "|";

            // Date range: yesterday to yesterday (today - 1)
            DateTime startDate = DateTime.Now.AddDays(-1);
            DateTime endDate = DateTime.Now.AddDays(-1);
         //  DateTime startDate = new DateTime(2022, 01, 01);
            //DateTime endDate = new DateTime(2026, 04, 25);

            try
            {
                // Set up SAP destination once
                RfcConfigParameters rfcPar = RFCconfing.rfcConfigparameters();
                RfcDestination dest = RfcDestinationManager.GetDestination(rfcPar);
                RfcRepository rfcrep = dest.Repository;

                Console.WriteLine($"Connected to SAP. Reading table: {sapTableName}");
                Console.WriteLine($"Date range: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd} on field {dateField}");

                // Parse field list from config
                List<string> fields = new List<string>();
                if (!string.IsNullOrWhiteSpace(fieldListConfig))
                {
                    fields = fieldListConfig.Split(',').Select(f => f.Trim()).ToList();
                }

                // Ensure table exists in Snowflake before date loop
                bool tableCreated = false;

                // Loop through each date
                for (DateTime date = startDate; date <= endDate; date = date.AddDays(1))
                {
                    string sapDate = date.ToString("yyyyMMdd");
                    string sfDate = date.ToString("yyyy-MM-dd");

                    Console.WriteLine($"\n--- Processing date: {sfDate} ---");

                    try
                    {
                        // Call RFC_READ_TABLE for this date
                        IRfcFunction rfcFunc = rfcrep.CreateFunction("RFC_READ_TABLE");
                        rfcFunc.SetValue("QUERY_TABLE", sapTableName);
                        rfcFunc.SetValue("DELIMITER", delimiter);
                        if (rowCount > 0)
                            rfcFunc.SetValue("ROWCOUNT", rowCount);
                        if (rowSkips > 0)
                            rfcFunc.SetValue("ROWSKIPS", rowSkips);

                        // FIELDS table
                        IRfcTable fieldsTable = rfcFunc.GetTable("FIELDS");
                        foreach (var f in fields)
                        {
                            fieldsTable.Append();
                            fieldsTable.SetValue("FIELDNAME", f);
                        }

                        // OPTIONS table - WHERE clause with date filter
                        IRfcTable optionsTable = rfcFunc.GetTable("OPTIONS");
                        optionsTable.Append();
                        optionsTable.SetValue("TEXT", $"{dateField} = '{sapDate}'");

                        // Invoke RFC
                        rfcFunc.Invoke(dest);

                        // Read FIELDS metadata back
                        IRfcTable retFields = rfcFunc.GetTable("FIELDS");
                        var colMetadata = new List<(string Name, int Offset, int Length)>();

                        for (int i = 0; i < retFields.RowCount; i++)
                        {
                            retFields.CurrentIndex = i;
                            string name = retFields.GetString("FIELDNAME").Trim();
                            int offset = retFields.GetInt("OFFSET");
                            int length = retFields.GetInt("LENGTH");
                            colMetadata.Add((name, offset, length));
                        }

                        // Read DATA rows
                        IRfcTable dataTable = rfcFunc.GetTable("DATA");

                        if (dataTable.RowCount <= 0)
                        {
                            Console.WriteLine($"  No data for {sfDate}, skipping.");
                            continue;
                        }

                        Console.WriteLine($"  Fetched {dataTable.RowCount} rows from SAP");

                        // Build DataTable from RFC response
                        DataTable dt = new DataTable(snowflakeTableName);
                        foreach (var col in colMetadata)
                        {
                            dt.Columns.Add(col.Name, typeof(string));
                        }
                        dt.Columns.Add("EXTRACT_DATE", typeof(DateTime)).DefaultValue = DateTime.Now.ToString("yyyy-MM-dd");

                        for (int i = 0; i < dataTable.RowCount; i++)
                        {
                            dataTable.CurrentIndex = i;
                            string wa = dataTable.GetString("WA");

                            DataRow row = dt.NewRow();
                            for (int c = 0; c < colMetadata.Count; c++)
                            {
                                int off = colMetadata[c].Offset;
                                int len = colMetadata[c].Length;
                                if (off + len > wa.Length) len = Math.Max(0, wa.Length - off);
                                row[c] = off < wa.Length ? wa.Substring(off, len).Trim() : "";
                            }
                            dt.Rows.Add(row);
                        }

                        // Column metadata for Snowflake COPY INTO
                        string colNames = string.Join(", ", dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
                        string colPositions = string.Join(", ", Enumerable.Range(1, dt.Columns.Count).Select(i => $"${i}"));

                        // Stream rows to CSV
                        string tempDir = Path.GetTempPath();
                        string csvFileName = $"{snowflakeTableName}_{sapDate}.csv";
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

                        // Collect distinct MATNRs for this batch (used for delete step)
                        List<string> matnrs = dt.AsEnumerable()
                            .Select(r => r["MATNR"]?.ToString())
                            .Where(m => !string.IsNullOrEmpty(m))
                            .Distinct()
                            .ToList();

                        dt.Dispose();

                        // Load to Snowflake
                        using (SnowflakeDbConnection sfCon = new SnowflakeDbConnection())
                        {
                            sfCon.ConnectionString = sfConnStr;
                            sfCon.Open();

                            // Step 1: Create table if not exists (only check once)
                            if (!tableCreated)
                            {
                                bool tableExists = false;
                                using (IDbCommand chkCmd = sfCon.CreateCommand())
                                {
                                    chkCmd.CommandText = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE UPPER(TABLE_NAME) = '{snowflakeTableName.ToUpper()}' AND TABLE_SCHEMA = CURRENT_SCHEMA()";
                                    int cnt = Convert.ToInt32(chkCmd.ExecuteScalar());
                                    tableExists = cnt > 0;
                                }

                                if (!tableExists)
                                {
                                    StringBuilder createSql = new StringBuilder();
                                    createSql.Append($"CREATE TABLE {snowflakeTableName} (");
                                    for (int i = 0; i < colMetadata.Count; i++)
                                    {
                                        createSql.Append($"{colMetadata[i].Name} VARCHAR({Math.Max(colMetadata[i].Length, 1)})");
                                        createSql.Append(", ");
                                    }
                                    createSql.Append("EXTRACT_DATE DATE");
                                    createSql.Append(")");

                                    using (IDbCommand createCmd = sfCon.CreateCommand())
                                    {
                                        createCmd.CommandText = createSql.ToString();
                                        createCmd.ExecuteNonQuery();
                                    }
                                    Console.WriteLine($"  Created table {snowflakeTableName} in Snowflake");
                                }
                                tableCreated = true;
                            }

                            // Step 2: Delete existing rows in Snowflake for the MATNRs fetched in this date's run
                            if (matnrs.Count > 0)
                            {
                                const int batchSize = 1000;
                                int totalDeleted = 0;
                                for (int i = 0; i < matnrs.Count; i += batchSize)
                                {
                                    var chunk = matnrs.Skip(i).Take(batchSize)
                                        .Select(m => $"'{m.Replace("'", "''")}'");
                                    string matnrList = string.Join(",", chunk);

                                    using (IDbCommand delCmd = sfCon.CreateCommand())
                                    {
                                        delCmd.CommandText = $"DELETE FROM {snowflakeTableName} WHERE MATNR IN ({matnrList})";
                                        totalDeleted += delCmd.ExecuteNonQuery();
                                    }
                                }
                                if (totalDeleted > 0)
                                    Console.WriteLine($"  Deleted {totalDeleted} existing rows for {matnrs.Count} MATNRs (date {sfDate})");
                            }

                            // Step 3: Append new rows via PUT + COPY INTO
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

                        Console.WriteLine($"  Loaded {totalRows} rows into Snowflake for {sfDate}");

                        // Clean up temp CSV
                        if (File.Exists(csvFilePath))
                            File.Delete(csvFilePath);
                    }
                    catch (Exception dateEx)
                    {
                        Console.WriteLine($"  Error for date {sfDate}: {dateEx.Message}");

                        // If SAP rejected the field list, probe each field individually to find the culprit(s)
                        if (dateEx.Message != null && dateEx.Message.IndexOf("FIELD_NOT_VALID", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Console.WriteLine("  Probing field list to identify invalid field(s)...");
                            var invalidFields = new List<string>();
                            foreach (var f in fields)
                            {
                                try
                                {
                                    IRfcFunction probe = rfcrep.CreateFunction("RFC_READ_TABLE");
                                    probe.SetValue("QUERY_TABLE", sapTableName);
                                    probe.SetValue("DELIMITER", delimiter);
                                    probe.SetValue("ROWCOUNT", 1);

                                    IRfcTable pf = probe.GetTable("FIELDS");
                                    pf.Append();
                                    pf.SetValue("FIELDNAME", f);

                                    probe.Invoke(dest);
                                }
                                catch (Exception probeEx)
                                {
                                    if (probeEx.Message != null && probeEx.Message.IndexOf("FIELD_NOT_VALID", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        invalidFields.Add(f);
                                        Console.WriteLine($"    [INVALID] {f} -> not a column of {sapTableName}");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"    [WARN]    {f} -> {probeEx.Message}");
                                    }
                                }
                            }

                            if (invalidFields.Count > 0)
                                Console.WriteLine($"  Invalid field(s) for {sapTableName}: {string.Join(", ", invalidFields)}");
                            else
                                Console.WriteLine("  No individual field flagged FIELD_NOT_VALID — check OPTIONS clause syntax instead.");
                        }
                    }
                }

                RFC_MESSAGE = "Successfully Inserted";
                Console.WriteLine($"\n{RFC_MESSAGE}");
            }
            catch (Exception ex)
            {
                RFC_MESSAGE = ex.Message;
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
