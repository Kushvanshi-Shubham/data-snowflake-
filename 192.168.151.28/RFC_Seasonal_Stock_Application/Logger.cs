using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RFC_Seasonal_Stock_Application
{
    public class Logger
    {
        private readonly string _connectionString;
        private readonly string _rfcname;
        private readonly string _store;

        public Logger(string connectionString, string RFC_NAME, string store)
        {
            _connectionString = connectionString;
            _rfcname = RFC_NAME;
            _store = store;
        }

        public void Log(LogEntry logEntry)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    connection.Open();

                    // Query to check if the record exists with the same RAC_DATE, RFC_NAME, and STORE
                    string selectQuery = @"
                    SELECT COUNT(1) 
                    FROM Tbl_SAP 
                    WHERE RFC_NAME = @RFC_NAME AND STORE = @STORE AND CAST(RAC_DATE AS DATE) = CAST(@RAC_DATE AS DATE)
                ";

                    using (SqlCommand command = new SqlCommand(selectQuery, connection))
                    {
                        // Add parameters for SELECT query
                        AddParameter(command, "@RFC_NAME", _rfcname);
                        AddParameter(command, "@STORE", _store);
                        AddParameter(command, "@RAC_DATE", logEntry.SAP_START_TIME); // Assuming SAP_START_TIME is the relevant date field

                        // Execute SELECT to check if the record exists
                        int recordCount = (int)command.ExecuteScalar();

                        string query = string.Empty;

                        // If record exists, update it; otherwise, insert it
                        if (recordCount > 0)
                        {
                            // Update existing record
                            query = @"
                            UPDATE Tbl_SAP
                            SET SAP_START_TIME = @SAP_START_TIME,
                                SAP_END_TIME = @SAP_END_TIME,
                                SQL_START = @SQL_START,
                                SQL_END = @SQL_END,
                                SAP_ROW_FETCH_COUNT = @SAP_ROW_FETCH_COUNT,
                                SAP_ROW_PUSH_COUNT = @SAP_ROW_PUSH_COUNT,
                                Message = @Message,
                                STATUS = @STATUS
                            WHERE RFC_NAME = @RFC_NAME AND STORE = @STORE AND CAST(RAC_DATE AS DATE) = CAST(@RAC_DATE AS DATE)
                        ";
                        }
                        else
                        {
                            // Insert new record
                            query = @"
                            INSERT INTO Tbl_SAP (RFC_NAME, RAC_DATE, STORE, SAP_START_TIME, SAP_END_TIME, 
                                                 SQL_START, SQL_END, SAP_ROW_FETCH_COUNT, SAP_ROW_PUSH_COUNT, 
                                                 Message, STATUS)
                            VALUES (@RFC_NAME, @RAC_DATE, @STORE, @SAP_START_TIME, @SAP_END_TIME, 
                                    @SQL_START, @SQL_END, @SAP_ROW_FETCH_COUNT, @SAP_ROW_PUSH_COUNT, 
                                    @Message, @STATUS)
                        ";
                        }

                        // Execute the corresponding query (either UPDATE or INSERT)
                        using (SqlCommand command2 = new SqlCommand(query, connection))
                        {
                            // Add parameters for the INSERT/UPDATE query
                            AddParameter(command2, "@RFC_NAME", _rfcname);
                            AddParameter(command2, "@RAC_DATE", logEntry.SAP_START_TIME); // Assuming SAP_START_TIME is the relevant date field
                            AddParameter(command2, "@STORE", _store);
                            AddParameter(command2, "@SAP_START_TIME", logEntry.SAP_START_TIME == default ? (object)DBNull.Value : logEntry.SAP_START_TIME);
                            AddParameter(command2, "@SAP_END_TIME", logEntry.SAP_END_TIME == default ? (object)DBNull.Value : logEntry.SAP_END_TIME);
                            AddParameter(command2, "@SQL_START", logEntry.SQL_START == default ? (object)DBNull.Value : logEntry.SQL_START);
                            AddParameter(command2, "@SQL_END", logEntry.SQL_END == default ? (object)DBNull.Value : logEntry.SQL_END);
                            AddParameter(command2, "@SAP_ROW_FETCH_COUNT", logEntry.SAP_ROW_FETCH_COUNT);
                            AddParameter(command2, "@SAP_ROW_PUSH_COUNT", logEntry.SAP_ROW_PUSH_COUNT);
                            AddParameter(command2, "@Message", logEntry.Message);
                            AddParameter(command2, "@STATUS", logEntry.Status);

                            // Execute the query (either INSERT or UPDATE)
                            command2.ExecuteNonQuery();
                        }
                    }

                    Console.WriteLine("Log entry has been processed (inserted or updated).");
                   
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error logging message: {ex.Message}");
            }
        }

        private void AddParameter(SqlCommand command, string paramName, object value)
        {
            command.Parameters.AddWithValue(paramName, value ?? DBNull.Value);
        }
    }


    //public void Log(LogEntry logEntry)
    //{
    //    try
    //    {


    //        using (SqlConnection connection = new SqlConnection(_connectionString))
    //        {
    //            connection.Open();

    //            string query = @"
    //                              INSERT INTO Tbl_SAP (RFC_NAME, RAC_DATE, STORE, SAP_START_TIME, SAP_END_TIME, 
    //                                        SQL_START, SQL_END, SAP_ROW_FETCH_COUNT, SAP_ROW_PUSH_COUNT, 
    //                                        Message, STATUS)
    //                VALUES (@RFC_NAME, @RAC_DATE, @STORE, @SAP_START_TIME, @SAP_END_TIME, 
    //                        @SQL_START, @SQL_END, @SAP_ROW_FETCH_COUNT, @SAP_ROW_PUSH_COUNT, 
    //                        @Message, @STATUS)";

    //            using (SqlCommand command = new SqlCommand(query, connection))
    //            {
    //                //AddParameter(command, "@ID", logEntry.ID ?? Guid.NewGuid().ToString());
    //                AddParameter(command, "@RFC_NAME", _rfcname);
    //                AddParameter(command, "@RAC_DATE", DateTime.Now);
    //                AddParameter(command, "@STORE", _store);
    //                AddParameter(command, "@SAP_START_TIME", logEntry.SAP_START_TIME == default ? (object)DBNull.Value : logEntry.SAP_START_TIME);
    //                AddParameter(command, "@SAP_END_TIME", logEntry.SAP_END_TIME == default ? (object)DBNull.Value : logEntry.SAP_END_TIME);
    //                AddParameter(command, "@SQL_START", logEntry.SQL_START == default ? (object)DBNull.Value : logEntry.SQL_START);
    //                AddParameter(command, "@SQL_END", logEntry.SQL_END == default ? (object)DBNull.Value : logEntry.SQL_END);
    //                AddParameter(command, "@SAP_ROW_FETCH_COUNT", logEntry.SAP_ROW_FETCH_COUNT);
    //                AddParameter(command, "@SAP_ROW_PUSH_COUNT", logEntry.SAP_ROW_PUSH_COUNT);
    //                AddParameter(command, "@Message", logEntry.Message);
    //                AddParameter(command, "@STATUS", logEntry.Status);


    //                command.ExecuteNonQuery();
    //            }
    //            Console.ReadLine();
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"Error logging message: {ex.Message}");
    //   }
    //}

    //private void AddParameter(SqlCommand command, string paramName, object value)
    //    {
    //        command.Parameters.AddWithValue(paramName, value ?? DBNull.Value);
    //    }
    //}

}
