using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ExcelDataReader;
using Snowflake.Data.Client;


class Program
{
    static string RFC_NAME { get; set; }
    static DateTime DATA_PULLFOR_DATE { get; set; }
    static string STORE { get; set; }
    static DateTime RFC_START_TIME { get; set; }
    static DateTime G_RFC_START_TIME { get; set; }
    static DateTime RFC_END_TIME { get; set; }
    static Int64 RFC_PULL_Records { get; set; }
    static Int64 RFC_PUSH_Records { get; set; }
    static string RFC_MESSAGE { get; set; }
    static string SAP_TABLE { get; set; }
    static DateTime SQL_START_TIME { get; set; }
    static DateTime SQL_END_TIME { get; set; }
    static Int64 SQL_PULL_Records { get; set; }
    static Int64 SQL_PUSH_Records { get; set; }
    static string SQL_MESSAGE { get; set; }
    static bool IsSuccess { get; set; }

    // ---------- Logging ----------
    static readonly object _logLock = new object();
    static string _logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
    static string LogFilePath => Path.Combine(_logDir, $"MIS_Import_{DateTime.Now:yyyyMMdd}.log");

    static void LogInfo(string msg) => Log("INFO", msg, null);
    static void LogWarn(string msg) => Log("WARN", msg, null);
    static void LogError(string msg, Exception ex) => Log("ERROR", msg, ex);

    static void Log(string level, string msg, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(_logDir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
            if (ex != null)
            {
                line += Environment.NewLine +
                        $"Exception: {ex.GetType().FullName}" + Environment.NewLine +
                        $"Message  : {ex.Message}" + Environment.NewLine +
                        $"Stack    : {ex.StackTrace}" + Environment.NewLine;
                if (ex.InnerException != null)
                {
                    line += "InnerException:" + Environment.NewLine +
                            ex.InnerException.ToString() + Environment.NewLine;
                }
            }

            lock (_logLock)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }

            // Optional: show errors on console too
            if (level == "ERROR")
                Console.WriteLine(line);
        }
        catch
        {
            // Never let logging crash the app
        }
    }
    // ----------------------------
    static bool WaitForDirectory(string path, int retries = 6, int delaySeconds = 10)
    {
        for (int i = 1; i <= retries; i++)
        {
            try
            {
                if (Directory.Exists(path)) return true;
            }
            catch { /* ignore */ }

            LogWarn($"Directory not accessible yet (try {i}/{retries}): {path}");
            System.Threading.Thread.Sleep(delaySeconds * 1000);
        }
        return false;
    }
    static void Main(string[] args)
    {

        // Catch unexpected crashes too
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogError("Unhandled exception occurred.", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        };

        //LogInfo("==== MIS Import Job Started ====");

        //string parentDirectory = @"\\file\0-V2\04-DEPARTMENT\04-PLANNING\01-CENTRAL PLANNING\13-RELEASED-BUDGETS\01-MIS";
        //string parentDirectory1 = @"\\File\0-v2\04-DEPARTMENT\04-PLANNING\01-CENTRAL PLANNING\02-REPORTS\16-RETAIL_GND_BGT_FORMAT";
        string[] parentDirectories =
   {
            //@"\\File\0-v2\04-DEPARTMENT\04-PLANNING\01-CENTRAL PLANNING\02-REPORTS",
        @"\\file\0-V2\04-DEPARTMENT\04-PLANNING\01-CENTRAL PLANNING\13-RELEASED-BUDGETS\01-MIS"

    };
        string connectionString = ConfigurationManager.ConnectionStrings["Snowflake"].ConnectionString;
        DateTime todayStart = DateTime.Today;
        foreach (string parentDirectory in parentDirectories)
        {
            // Enable ExcelDataReader to support .xlsx
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            LogInfo($"Running as: {Environment.UserDomainName}\\{Environment.UserName}");
            LogInfo($"BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");

            try
            {
                LogInfo($"UNC exists? {parentDirectory} => {Directory.Exists(parentDirectory)}");
            }
            catch (Exception ex)
            {
                LogError($"UNC check failed for: {parentDirectory}", ex);
            }
            if (!WaitForDirectory(parentDirectory, retries: 6, delaySeconds: 10))
            {
                LogError($"Parent directory not found after retries: {parentDirectory}",
                    new DirectoryNotFoundException(parentDirectory));
                return;
            }
            try
            {
                if (!Directory.Exists(parentDirectory))
                {
                    LogError($"Parent directory not found: {parentDirectory}", new DirectoryNotFoundException(parentDirectory));
                    return;
                }

                string[] directories = Directory.GetDirectories(parentDirectory);

                string[] specificDirectories = directories
                    .Where(d =>
                    //Path.GetFileName(d) == "ALC_ARTICLE" ||
                    //Path.GetFileName(d) == "Avg_Denst" ||
                    //Path.GetFileName(d) == "Planogram" ||
                    //Path.GetFileName(d) == "ST_FLR_DIV_FIX_VETTING" ||
                    //Path.GetFileName(d) == "16-RETAIL_GND_BGT_FORMAT" ||
                    //    Path.GetFileName(d).ToLower() == "co_bgt_ak" ||
                    //    Path.GetFileName(d).ToLower() == "da_article" ||
                    //    Path.GetFileName(d).ToLower() == "mj_sz_group" ||
                        Path.GetFileName(d).ToLower() == "sale-bgt"
                        //Path.GetFileName(d).ToLower() == "pur-bgt" ||
                        //Path.GetFileName(d).ToLower() == "space-bgt" ||
                        //Path.GetFileName(d).ToLower() == "c-art" ||
                        //Path.GetFileName(d).ToLower() == "store_nt_bgt" ||
                        //Path.GetFileName(d).ToLower() == "cont" ||
                        //Path.GetFileName(d).ToLower() == "ref_fest_master" ||
                        //Path.GetFileName(d).ToLower() == "auto_art" ||
                        //Path.GetFileName(d).ToLower() == "c-art" ||
                        //Path.GetFileName(d).ToLower() == "kuddi_art" ||
                        //Path.GetFileName(d).ToLower() == "l_article" ||
                        //Path.GetFileName(d).ToLower() == "ALC_ARTICLE"
                    )
                    .ToArray();

                DateTime currentTime = DateTime.Now;
                LogInfo($"Directories matched: {specificDirectories.Length}");

                foreach (var directory in specificDirectories)
                {
                    string directoryNameSafe = null;

                    try
                    {
                        directoryNameSafe = Path.GetFileName(directory).Replace("-", "_");
                        LogInfo($"Scanning directory: {directory} (table prefix: {directoryNameSafe})");
                        //string[] files = Directory.GetFiles(directory, "ST_MAJ_CAT_SALE_DISP_PUR_PLAN*.*", SearchOption.TopDirectoryOnly);
                       string[] files = Directory.GetFiles(directory);

                        foreach (var file in files)
                        {
                            try
                            {
                                FileInfo fileInfo = new FileInfo(file);
                                DateTime lastUpdateTime = fileInfo.LastWriteTime;
                                TimeSpan difference = currentTime - lastUpdateTime;

                                DateTime lastWriteTime = fileInfo.LastWriteTime;
                                DateTime creationTime = fileInfo.CreationTime;

                                // ===== FILTER: Only files created or modified TODAY =====
                                if (lastWriteTime < todayStart && creationTime < todayStart)
                                {
                                    // LogInfo($"Skipped (not today): {file} | Created: {creationTime:yyyy-MM-dd HH:mm:ss} | Modified: {lastWriteTime:yyyy-MM-dd HH:mm:ss}");
                                    continue;
                                }
                                // If you want the 24-hour filter, uncomment:
                                // if (difference.TotalHours > 24) continue;

                                LogInfo($"Processing file: {file} | LastWriteTime: {lastUpdateTime:yyyy-MM-dd HH:mm:ss}");

                                // Use FileShare.ReadWrite to avoid issues if file is open by someone
                                using (var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                using (var reader = ExcelReaderFactory.CreateReader(stream))
                                {
                                    var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                                    {
                                        ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
                                        {
                                            UseHeaderRow = true
                                        }
                                    });

                                    foreach (DataTable table in result.Tables)
                                    {
                                        try
                                        {
                                            LogInfo($"Worksheet: {table.TableName} | Rows: {table.Rows.Count} | Cols: {table.Columns.Count}");

                                            EnsureTableExists(directoryNameSafe, table, connectionString);
                                            InsertDataIntoDatabase(directoryNameSafe, table, connectionString);

                                            LogInfo($"Completed worksheet: {table.TableName} from file: {Path.GetFileName(file)}");
                                        }
                                        catch (Exception exSheet)
                                        {
                                            LogError($"Error processing worksheet '{table.TableName}' in file '{file}' (dir '{directoryNameSafe}').", exSheet);
                                        }
                                    }
                                }
                            }
                            catch (Exception exFile)
                            {
                                LogError($"Error processing file '{file}' in directory '{directory}'.", exFile);
                            }
                        }
                    }
                    catch (Exception exDir)
                    {
                        LogError($"Error reading directory '{directory}' (prefix '{directoryNameSafe ?? "unknown"}').", exDir);
                    }
                }

                LogInfo("==== MIS Import Job Completed Successfully ====");
            }
            catch (Exception ex)
            {
                LogError("Fatal error in Main().", ex);
            }
            finally
            {
                LogInfo("==== MIS Import Job Ended ====");
            }
        }
    }
    static string GetSafeSqlTableName(string directoryName, string sheetName)
    {
        string safeDirectoryName = directoryName.Trim().Replace("-", "_").Replace(" ", "_");
        string safeSheetName = sheetName.Trim().Replace("-", "_").Replace(" ", "_");

        if (!string.IsNullOrEmpty(safeDirectoryName) && char.IsDigit(safeDirectoryName[0]))
        {
            safeDirectoryName = "TBL_" + safeDirectoryName;
        }

        return $"{safeDirectoryName}_{safeSheetName}";
    }
    static void EnsureTableExists(string directoryName, DataTable dataTable, string connectionString)
    {
        string tableName = GetSafeSqlTableName(directoryName, dataTable.TableName);

        try
        {
            using (var sfConnection = new SnowflakeDbConnection())
            {
                sfConnection.ConnectionString = connectionString;
                sfConnection.Open();

                // Drop if exists
                string dropQuery = $"DROP TABLE IF EXISTS {tableName}";
                using (var command = sfConnection.CreateCommand())
                {
                    command.CommandText = dropQuery;
                    command.ExecuteNonQuery();
                }
                LogInfo($"Dropped table (if existed): {tableName}");

                // Create with auto-increment ID column
                string createQuery = $"CREATE TABLE {tableName} ({GenerateTableSchema(dataTable)}, \"ID\" NUMBER AUTOINCREMENT START 1 INCREMENT 1)";
                using (var command = sfConnection.CreateCommand())
                {
                    command.CommandText = createQuery;
                    command.ExecuteNonQuery();
                }
                LogInfo($"Created table: {tableName}");
            }
        }
        catch (Exception ex)
        {
            LogError($"EnsureTableExists failed for table '{tableName}'.", ex);
            throw; // rethrow so caller can log worksheet-level context too (already does)
        }
    }

    static string ConvertDateFormat(string input)
    {
        DateTime tempDate;
        if (DateTime.TryParse(input, out tempDate))
        {
            return tempDate.ToString("yyyy-MM-dd").Trim();
        }
        else
        {
            return input.Trim();
        }
    }

    static string GenerateTableSchema(DataTable dataTable)
    {
        var columnDefinitions = new StringBuilder();

        foreach (DataColumn column in dataTable.Columns)
        {
            string colName = ConvertDateFormat(column.ColumnName);
            string columnType = MapColumnType(column.DataType);

            if (column.ColumnName == "MC_CD" || column.ColumnName == "M_VND_CD")
                columnType = "VARCHAR";

            columnDefinitions.Append($"\"{colName}\" {columnType},");
        }

        if (columnDefinitions.Length > 0)
            columnDefinitions.Length -= 1;

        return columnDefinitions.ToString();
    }

    static string MapColumnType(Type dataType)
    {
        return dataType switch
        {
            Type t when t == typeof(string) => "VARCHAR",
            Type t when t == typeof(int) => "NUMBER(10,0)",
            Type t when t == typeof(long) => "NUMBER(19,0)",
            Type t when t == typeof(decimal) => "NUMBER(18,2)",
            Type t when t == typeof(double) => "FLOAT",
            Type t when t == typeof(DateTime) => "DATE",
            Type t when t == typeof(bool) => "BOOLEAN",
            _ => "VARCHAR"
        };
    }

    static void InsertDataIntoDatabase(string directoryName, DataTable dataTable, string connectionString)
    {
        string destTable = GetSafeSqlTableName(directoryName, dataTable.TableName);
        string csvFilePath = null;
        try
        {
            // Normalize column names BEFORE COPY INTO column list + write
            foreach (DataColumn column in dataTable.Columns)
            {
                column.ColumnName = ConvertDateFormat(column.ColumnName);
            }

            if (dataTable.Rows.Count == 0)
            {
                LogInfo($"No rows to insert into {destTable}, skipping.");
                return;
            }

            // Stream rows to CSV
            string tempDir = Path.GetTempPath();
            string csvFileName = $"{destTable}_{DateTime.Now:yyyyMMddHHmmssfff}.csv";
            csvFilePath = Path.Combine(tempDir, csvFileName);

            using (StreamWriter sw = new StreamWriter(csvFilePath, false, Encoding.UTF8))
            {
                foreach (DataRow dataRow in dataTable.Rows)
                {
                    var csvFields = new List<string>();
                    foreach (DataColumn col in dataTable.Columns)
                    {
                        object val = dataRow[col];
                        if (val == null || val == DBNull.Value)
                            csvFields.Add("");
                        else if (val is string strVal)
                            csvFields.Add($"\"{strVal.Replace("\"", "\"\"")}\"");
                        else if (val is DateTime dtVal)
                            csvFields.Add($"\"{dtVal:yyyy-MM-dd}\"");
                        else if (val is decimal || val is double || val is float)
                            csvFields.Add(Convert.ToString(val, CultureInfo.InvariantCulture));
                        else
                            csvFields.Add($"\"{val.ToString().Replace("\"", "\"\"")}\"");
                    }
                    sw.WriteLine(string.Join(",", csvFields));
                }
            }

            string colNames = string.Join(", ", dataTable.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\""));
            string colPositions = string.Join(", ", Enumerable.Range(1, dataTable.Columns.Count).Select(i => $"${i}"));

            using (var sfConnection = new SnowflakeDbConnection())
            {
                sfConnection.ConnectionString = connectionString;
                sfConnection.Open();

                string putPath = csvFilePath.Replace("\\", "/");
                using (var putCmd = sfConnection.CreateCommand())
                {
                    putCmd.CommandText = $"PUT 'file://{putPath}' @%{destTable} AUTO_COMPRESS=TRUE OVERWRITE=TRUE";
                    putCmd.ExecuteNonQuery();
                }

                using (var copyCmd = sfConnection.CreateCommand())
                {
                    copyCmd.CommandText =
                        $"COPY INTO {destTable} ({colNames}) " +
                        $"FROM (SELECT {colPositions} FROM @%{destTable}/{csvFileName}.gz) " +
                        "FILE_FORMAT=(TYPE='CSV' FIELD_OPTIONALLY_ENCLOSED_BY='\"' " +
                        "NULL_IF=('') EMPTY_FIELD_AS_NULL=TRUE) PURGE=TRUE";
                    copyCmd.ExecuteNonQuery();
                }
            }

            LogInfo($"Inserted rows: {dataTable.Rows.Count} into {destTable}");
        }
        catch (Exception ex)
        {
            LogError($"InsertDataIntoDatabase failed for table '{destTable}'.", ex);
            throw;
        }
        finally
        {
            try
            {
                if (csvFilePath != null && File.Exists(csvFilePath))
                    File.Delete(csvFilePath);
            }
            catch { /* ignore cleanup errors */ }
        }
    }
}
