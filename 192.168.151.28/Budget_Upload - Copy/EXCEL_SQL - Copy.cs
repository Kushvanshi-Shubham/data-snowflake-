using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Text;
using ExcelDataReader;
using CommonUtil;
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
    static void Main1(string[] args)
    {
        //CommonUtil.CommonUtil myObj_master = new CommonUtil.CommonUtil();
        //CommonUtil.CommonUtil myObj = new CommonUtil.CommonUtil();

        string parentDirectory = @"\\file\0-V2\04-DEPARTMENT\04-PLANNING\13-RELEASED-BUDGETS\01-MIS";
        //parentDirectory= @"\\mis\01-MIS\02-ALL-BGT";

        //string excelFilePath = @"\\file\0-V2\04-DEPARTMENT\04-PLANNING\13-RELEASED-BUDGETS\01-MIS\Sale-BGT\ST_MAJ_CAT_SEG_PLAN.xlsx";
        string connectionString = "Server=192.168.151.28;Database=DataV2;uid=sa;Password=vrl@55555;TrustServerCertificate=True;";
        string[] directories = Directory.GetDirectories(parentDirectory);

        //string[] specificDirectories = directories
        //   .Where(d => Path.GetFileName(d).ToLower() == "C-Art".ToLower() || Path.GetFileName(d).ToLower() == "L_ARTICLE".ToLower() )
        //   .ToArray();
        string[] specificDirectories = directories
       //.Where(d => Path.GetFileName(d) == "Store_NT_BGT").ToArray();
       .Where(d => Path.GetFileName(d).ToLower() == "SALE-BGT".ToLower() || Path.GetFileName(d).ToLower() == "PUR-BGT".ToLower() || Path.GetFileName(d).ToLower() == "SPACE-BGT".ToLower() || Path.GetFileName(d).ToLower() == "C-Art".ToLower() || Path.GetFileName(d).ToLower() == "Store_NT_BGT".ToLower() || Path.GetFileName(d).ToLower() == "CONT".ToLower() || Path.GetFileName(d).ToLower() == "REF_FEST_MASTER".ToLower() || Path.GetFileName(d).ToLower() == "AUTO_ART".ToLower() || Path.GetFileName(d).ToLower() == "C-ART".ToLower() || Path.GetFileName(d).ToLower() == "KUDDI_ART".ToLower() || Path.GetFileName(d).ToLower() == "L_ARTICLE".ToLower())
       .ToArray();
        // Enable ExcelDataReader to support .xlsx
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        try
        {
            DateTime currentTime = DateTime.Now;

            foreach (var directory in specificDirectories)
            {
                try
                {

                    string[] files = Directory.GetFiles(directory);
                    //      string[] specificFiles = files
                    //.Where(d => Path.GetFileName(d).ToLower() == "ST_MC_VND_PLAN.xlsx".ToLower())
                    //.ToArray();

                    foreach (var file in files)
                    {
                        try
                        {
                            FileInfo fileInfo = new FileInfo(file);
                            DateTime lastUpdateTime = fileInfo.LastWriteTime;

                            TimeSpan difference = currentTime - lastUpdateTime;

                            //if (difference.TotalHours<=24)
                            //{
                            string directoryName = System.IO.Path.GetFileName(directory).Replace("-", "_");
                            string excelFilePath = file;
                            using (var stream = File.Open(excelFilePath, FileMode.Open, FileAccess.Read))
                            {
                                // Read the Excel file
                                using (var reader = ExcelReaderFactory.CreateReader(stream))
                                {
                                    var result = reader.AsDataSet(new ExcelDataSetConfiguration()
                                    {
                                        ConfigureDataTable = (_) => new ExcelDataTableConfiguration()
                                        {
                                            UseHeaderRow = true // Treat the first row as a header
                                        }
                                    });

                                    foreach (DataTable table in result.Tables)
                                    {
                                        Console.WriteLine($"Processing worksheet: {table.TableName}");
                                        EnsureTableExists(directoryName, table, connectionString);
                                        InsertDataIntoDatabase(directoryName, table, connectionString);
                                    }
                                }
                            }
                            //}

                        }
                        catch (Exception ex) { }

                    }
                }
                catch (Exception ex) { }
            }
        }
        catch (Exception ex)
        {
        }
    }

    static void EnsureTableExists(string directoryName, DataTable dataTable, string connectionString)
    {

        string tableName = directoryName + "_" + dataTable.TableName;

        using (var sqlConnection = new SqlConnection(connectionString))
        {
            sqlConnection.Open();

            // Check if the table exists
            //string checkTableQuery = $@"
            //IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{tableName}')
            //BEGIN
            //    CREATE TABLE {tableName} ({GenerateTableSchema(dataTable)});
            //END 
            //else
            //begin
            //    drop table {tableName};
            //    CREATE TABLE {tableName} ({GenerateTableSchema(dataTable)});
            //    ALTER TABLE [dbo].[{tableName}] ADD [ID] BIGINT IDENTITY(1,1);
            //    ALTER TABLE [dbo].[{tableName}] ADD  CONSTRAINT [PK_{tableName}] PRIMARY KEY CLUSTERED 
            //    (
            //    [ID] ASC
            //    )WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [V2R]
            //end";

            string checkTableQuery = $@"
            IF OBJECT_ID('{tableName}', 'U') IS NOT NULL DROP TABLE {tableName};
            ";
            using (var command = new SqlCommand(checkTableQuery, sqlConnection))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
                Console.WriteLine($"drop table '{tableName}'.");
            }

            checkTableQuery = $@"
            CREATE TABLE {tableName} ({GenerateTableSchema(dataTable)});
            ";

            using (var command = new SqlCommand(checkTableQuery, sqlConnection))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
                Console.WriteLine($"create table '{tableName}'.");
            }

            checkTableQuery = $@"
            ALTER TABLE [dbo].[{tableName}] ADD [ID] BIGINT IDENTITY(1,1);
            ALTER TABLE [dbo].[{tableName}] ADD  CONSTRAINT [PK_{tableName}] PRIMARY KEY CLUSTERED 
            (
            [ID] ASC
            )WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [V2R]
            ";

            using (var command = new SqlCommand(checkTableQuery, sqlConnection))
            {
                command.CommandTimeout = 0;
                command.ExecuteNonQuery();
                Console.WriteLine($"add ID column into the table '{tableName}' and create cluster index.");
            }
        }
    }
    static string ConvertDateFormat(string input)
    {
        DateTime tempDate;

        // Try to parse the input string as a date
        if (DateTime.TryParse(input, out tempDate))
        {
            // Return the date formatted in "yyyy-MM-dd"
            var k = tempDate.ToString("yyyy-MM-dd");
            return "" + tempDate.ToString("yyyy-MM-dd") + "".Trim();
        }
        else
        {
            return input.Trim();
        }

        // Return null if the input is not a valid date
        //return null;
    }

    static string GenerateTableSchema(DataTable dataTable)
    {
        var columnDefinitions = new StringBuilder();

        foreach (DataColumn column in dataTable.Columns)
        {
            string columnType = MapColumnType(column.DataType);
            if (column.ColumnName == "MC_CD" || column.ColumnName == "M_VND_CD")
            {
                columnDefinitions.Append($"[{ConvertDateFormat(column.ColumnName)}] {"NVARCHAR(MAX)"},");
            }
            else
            {
                columnDefinitions.Append($"[{ConvertDateFormat(column.ColumnName)}] {columnType},");
            }
        }

        // Remove trailing comma
        if (columnDefinitions.Length > 0)
        {
            columnDefinitions.Length -= 1;
        }

        return columnDefinitions.ToString();
    }

    static string MapColumnType(Type dataType)
    {
        // Map C# data types to SQL data types
        return dataType switch
        {

            Type t when t == typeof(string) => "NVARCHAR(MAX)",
            Type t when t == typeof(int) => "INT",
            Type t when t == typeof(long) => "BIGINT",
            Type t when t == typeof(decimal) => "DECIMAL(18,2)",
            Type t when t == typeof(double) => "FLOAT",
            Type t when t == typeof(DateTime) => "DATE",
            Type t when t == typeof(bool) => "BIT",
            _ => "NVARCHAR(MAX)" // Default to NVARCHAR for unsupported types
        };
    }

    static void InsertDataIntoDatabase(string directoryName, DataTable dataTable, string connectionString)
    {
        using (var sqlConnection = new SqlConnection(connectionString))
        {
            sqlConnection.Open();

            using (var bulkCopy = new SqlBulkCopy(sqlConnection))
            {
                bulkCopy.BulkCopyTimeout = 0;
                bulkCopy.DestinationTableName = directoryName + "_" + dataTable.TableName;

                // Map columns (if needed)
                foreach (DataColumn column in dataTable.Columns)
                {
                    var temp = ConvertDateFormat(column.ColumnName);
                    bulkCopy.ColumnMappings.Add(temp, temp);
                }
                int index = 1; // Counter for new column names
                foreach (DataColumn column in dataTable.Columns)
                {
                    column.ColumnName = ConvertDateFormat(column.ColumnName);
                    index++;
                }

                // Bulk insert into database
                bulkCopy.WriteToServer(dataTable);
                Console.WriteLine($"Inserted data from {dataTable.TableName} into database.");
            }
        }
    }
}
