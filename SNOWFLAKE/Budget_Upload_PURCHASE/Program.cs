using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using ExcelDataReader;
using Microsoft.Data.SqlClient;


using System;
using System.Data;
using System.Data.SqlClient;
using System.Text;
using ExcelDataReader;
using System.IO;
using System.Text.RegularExpressions;

using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using ExcelDataReader;
using System.Text.RegularExpressions;

class Program11
{
    static void Main1(string[] args)
    {
        string parentDirectory = @"\\file\0-V2\04-DEPARTMENT\04-PLANNING\13-RELEASED-BUDGETS\01-MIS";
      //  string filePath = @"\\file\0-V2\04-DEPARTMENT\04-PLANNING\13-RELEASED-BUDGETS\01-MIS\PUR-BGT\MAJ_CAT_PUR_QTY.xlsx";
        string connectionString = "Server=192.168.151.27;Database=DataV2;uid=sa;Password=vrl@55555;TrustServerCertificate=True;";
        string[] directories = Directory.GetDirectories(parentDirectory);
        try
        {
            Console.WriteLine("Starting");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            foreach (var directory in directories)
            {
                // Extract table name from the file path (Excel file name without extension)
             
                string[] files = Directory.GetFiles(directory);

                foreach (var file in files)
                {
                    string filePath = file;
                    string tableName = Path.GetFileNameWithoutExtension(filePath);
                    using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read))
                    {
                        var config = new ExcelReaderConfiguration
                        {
                            FallbackEncoding = Encoding.UTF8 // Handles encoding for older Excel files
                        };

                        using (var reader = ExcelReaderFactory.CreateReader(stream, config))
                        {
                            // Create a DataTable to hold the data from Excel before inserting it into the database
                            DataTable dataTable = new DataTable();
                            bool isHeaderRow = true;

                            int rowCount = 0;
                            while (reader.Read()) // Move to the next row
                            {
                                rowCount++;

                                // If it's the first row, create DataTable columns from the header row
                                if (isHeaderRow)
                                {
                                    for (int col = 0; col < reader.FieldCount; col++)
                                    {
                                        dataTable.Columns.Add(reader.GetValue(col).ToString()); // Column names based on first row
                                    }
                                    isHeaderRow = false;
                                }
                                else
                                {
                                    // Add data to the DataTable
                                    DataRow newRow = dataTable.NewRow();
                                    for (int col = 0; col < reader.FieldCount; col++)
                                    {
                                        newRow[col] = reader.GetValue(col) ?? DBNull.Value; // Handle null values
                                    }
                                    dataTable.Rows.Add(newRow);
                                }

                                // Periodically insert data into the database to avoid memory overload
                                if (rowCount % 1000 == 0 || reader.IsClosed)
                                {
                                    InsertDataIntoDatabase(dataTable, connectionString, tableName);
                                    dataTable.Clear(); // Clear DataTable to prepare for next batch
                                    Console.WriteLine($"Processed {rowCount} rows...");
                                }
                            }

                            // Insert any remaining rows
                            if (dataTable.Rows.Count > 0)
                            {
                                InsertDataIntoDatabase(dataTable, connectionString, tableName);
                            }

                            Console.WriteLine($"Processing complete. Total rows: {rowCount}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    // Method to insert data from DataTable into the SQL Server database
    static void InsertDataIntoDatabase(DataTable dataTable, string connectionString, string tableName)
    {
        try
        {
            // Step 1: Check if the table exists, if not create it
            if (!CheckIfTableExists(connectionString, tableName))
            {
                string createTableSQL = GenerateCreateTableSQL(dataTable, tableName);
                ExecuteSQL(createTableSQL, connectionString);
            }

            // Step 2: Insert data into the table using SqlBulkCopy (append if table exists)
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (SqlBulkCopy bulkCopy = new SqlBulkCopy(conn))
                {
                    bulkCopy.DestinationTableName = tableName; // Use dynamic table name

                    // Optional: Map columns if the column names in the DataTable don't match the database table
                    // Example: bulkCopy.ColumnMappings.Add("ExcelColumnName", "DatabaseColumnName");

                    bulkCopy.BatchSize = 1000; // Adjust based on your needs
                    bulkCopy.BulkCopyTimeout = 60; // Set a timeout for bulk copy
                    bulkCopy.WriteToServer(dataTable); // Insert data into the database
                    Console.WriteLine("Data inserted successfully");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error inserting data into database: {ex.Message}");
        }
    }

    // Method to generate CREATE TABLE SQL dynamically based on the DataTable schema
    static string GenerateCreateTableSQL(DataTable dataTable, string tableName)
    {
        StringBuilder sql = new StringBuilder();
        sql.AppendLine("CREATE TABLE [dbo].[" + tableName + "] (");

        foreach (DataColumn column in dataTable.Columns)
        {
            string columnName = column.ColumnName;
            string columnType = GetSqlDataType(column.DataType);
            sql.AppendLine($"    [{columnName}] {columnType},");
        }

        // Remove the last comma
        sql.Length--;
        sql.AppendLine(");");

        return sql.ToString();
    }

    // Method to check if a table exists
    static bool CheckIfTableExists(string connectionString, string tableName)
    {
        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            string checkTableSQL = $"SELECT CASE WHEN EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[{tableName}]') AND type = 'U') THEN 1 ELSE 0 END";
            using (SqlCommand cmd = new SqlCommand(checkTableSQL, conn))
            {
                return (int)cmd.ExecuteScalar() == 1;
            }
        }
    }

    // Method to get SQL data type based on C# data type
    static string GetSqlDataType(Type type)
    {
        if (type == typeof(string)) return "NVARCHAR(MAX)";
        if (type == typeof(int)) return "INT";
        if (type == typeof(decimal)) return "DECIMAL(18, 2)";
        if (type == typeof(DateTime)) return "DATETIME";
        if (type == typeof(bool)) return "BIT";
        // Add more mappings as needed
        return "NVARCHAR(MAX)"; // Default fallback
    }

    // Method to execute SQL command
    static void ExecuteSQL(string sql, string connectionString)
    {
        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            using (SqlCommand command = new SqlCommand(sql, conn))
            {
                command.ExecuteNonQuery();
                Console.WriteLine("Table created successfully.");
            }
        }
    }
}


