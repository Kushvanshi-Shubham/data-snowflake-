using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ExcelDataReader;


class Program1
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
    static void Main1(string[] args)
    {

        // Catch unexpected crashes too
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogError("Unhandled exception occurred.", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
        };

        //LogInfo("==== MIS Import Job Started ====");

        //string parentDirectory = @"\\file\0-V2\04-DEPARTMENT\04-PLANNING\01-CENTRAL PLANNING\13-RELEASED-BUDGETS\01-MIS";
        //string parentDirectory1 = @"\\File\0-v2\04-DEPARTMENT\04-PLANNING\01-CENTRAL PLANNING\02-REPORTS\16-RETAIL_GND_BGT_FORMAT";
        //     string[] parentDirectories =
        //{
        //         @"\\File\0-v2\04-DEPARTMENT\04-PLANNING\01-CENTRAL PLANNING\02-REPORTS",
        //     @"\\file\0-V2\04-DEPARTMENT\04-PLANNING\01-CENTRAL PLANNING\13-RELEASED-BUDGETS\01-MIS"

        // };
        string[] parentDirectories =
       {
            @"\\file\0-V2\04-DEPARTMENT\04-PLANNING\01-CENTRAL PLANNING\13-RELEASED-BUDGETS\03-WEEK WISE PURCHASE INPUT\01-MAJ",
            @"\\file\0-V2\04-DEPARTMENT\04-PLANNING\01-CENTRAL PLANNING\13-RELEASED-BUDGETS\03-WEEK WISE PURCHASE INPUT"



    }
        ;
        string connectionString = "Server=192.168.151.28;Database=planning;uid=datalake;Password=lQn@t-rm#W*NG7;TrustServerCertificate=True;";
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
                      Path.GetFileName(d) == "QTY" ||
                      Path.GetFileName(d) == "MASTER"

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
        //string tableName = directoryName + "_" + dataTable.TableName;
        //tableName=tableName.Replace("-", "_");
        string tableName = GetSafeSqlTableName(directoryName, dataTable.TableName);

        try
        {
            using (var sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();

                // Drop if exists
                string dropQuery = $@"IF OBJECT_ID('{tableName}', 'U') IS NOT NULL DROP TABLE {tableName};";
                using (var command = new SqlCommand(dropQuery, sqlConnection))
                {
                    command.CommandTimeout = 0;
                    command.ExecuteNonQuery();
                }
                LogInfo($"Dropped table (if existed): {tableName}");

                // Create
                string createQuery = $@"CREATE TABLE {tableName} ({GenerateTableSchema(dataTable)});";
                using (var command = new SqlCommand(createQuery, sqlConnection))
                {
                    command.CommandTimeout = 0;
                    command.ExecuteNonQuery();
                }
                LogInfo($"Created table: {tableName}");

                // Add ID + PK
                string alterQuery = $@"
ALTER TABLE [dbo].[{tableName}] ADD [ID] BIGINT IDENTITY(1,1);
ALTER TABLE [dbo].[{tableName}] ADD CONSTRAINT [PK_{tableName}] PRIMARY KEY CLUSTERED ([ID] ASC)
";
                using (var command = new SqlCommand(alterQuery, sqlConnection))
                {
                    command.CommandTimeout = 0;
                    command.ExecuteNonQuery();
                }
                LogInfo($"Added ID + PK for table: {tableName}");
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
                columnType = "NVARCHAR(MAX)";

            columnDefinitions.Append($"[{colName}] {columnType},");
        }

        if (columnDefinitions.Length > 0)
            columnDefinitions.Length -= 1;

        return columnDefinitions.ToString();
    }

    static string MapColumnType(Type dataType)
    {
        return dataType switch
        {
            Type t when t == typeof(string) => "NVARCHAR(MAX)",
            Type t when t == typeof(int) => "INT",
            Type t when t == typeof(long) => "BIGINT",
            Type t when t == typeof(decimal) => "DECIMAL(18,2)",
            Type t when t == typeof(double) => "FLOAT",
            Type t when t == typeof(DateTime) => "DATE",
            Type t when t == typeof(bool) => "BIT",
            _ => "NVARCHAR(MAX)"
        };
    }

    static void InsertDataIntoDatabase(string directoryName, DataTable dataTable, string connectionString)
    {
        //string destTable = directoryName + "_" + dataTable.TableName;
        //destTable=destTable.Replace("-", "_");
        string destTable = GetSafeSqlTableName(directoryName, dataTable.TableName);
        try
        {
            // Normalize column names BEFORE bulk copy mappings + write
            foreach (DataColumn column in dataTable.Columns)
            {
                column.ColumnName = ConvertDateFormat(column.ColumnName);
            }

            using (var sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();

                using (var bulkCopy = new SqlBulkCopy(sqlConnection))
                {
                    bulkCopy.BulkCopyTimeout = 0;
                    bulkCopy.DestinationTableName = destTable;

                    foreach (DataColumn column in dataTable.Columns)
                    {
                        bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                    }

                    bulkCopy.WriteToServer(dataTable);
                }
            }

            LogInfo($"Inserted rows: {dataTable.Rows.Count} into {destTable}");
        }
        catch (Exception ex)
        {
            LogError($"InsertDataIntoDatabase failed for table '{destTable}'.", ex);
            throw;
        }
    }
}
