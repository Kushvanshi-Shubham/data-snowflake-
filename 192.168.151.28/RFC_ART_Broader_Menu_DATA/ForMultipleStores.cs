using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;

namespace RFC_STOCK_DATA
{
    public static class ForMultipleStores
    {
        
        private static readonly Stopwatch MasterStopwatch = new Stopwatch();
        private static readonly Stopwatch TransactionStopwatch = new Stopwatch();


        private static (string StartTime, string EndTime, TimeSpan Duration, bool Success, string Remarks) MasterTiming
               = (string.Empty, string.Empty, TimeSpan.Zero, false, string.Empty);

        private static readonly List<(string StoreCode, string StartTime, string EndTime, TimeSpan Duration, bool Success, string Remarks)> TransactionTimings =
            new List<(string StoreCode, string StartTime, string EndTime, TimeSpan Duration, bool Success, string Remarks)>();


        public static void StartMasterProcess()
        {
            MasterStopwatch.Start();
            MasterTiming = (CurrentTime, "", TimeSpan.Zero, true, ""); 
        }

      
        public static void StopMasterProcess(bool success, string remarks)
        {
            MasterStopwatch.Stop();
            MasterTiming = (MasterTiming.StartTime, CurrentTime, MasterStopwatch.Elapsed, success, remarks);
        }

        private static string TransactionStartTime = string.Empty;

        public static void StartTransactionProcess()
        {
            TransactionStopwatch.Restart();
            TransactionStartTime = CurrentTime; // Capture start time here
        }

        public static void LogTransactionTiming(string storeCode, bool success, string remarks)
        {
            string endTime = CurrentTime; // Capture end time here
            TimeSpan duration = TransactionStopwatch.Elapsed;

            TransactionTimings.Add((storeCode, TransactionStartTime, endTime, duration, success, remarks));
            TransactionStopwatch.Stop();
        }


        public static void InsertMasterLog(string connectionString, string rfcName, string tableName, out int reportId)
        {
            reportId = 0;

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    string insertMasterLogQuery = @"
                        INSERT INTO [DataWarehouse].[dbo].[RFCReport]
                        ([RFC Name], [Table Name], [StartTime], [EndTime], [Duration], [Status], [Remarks])
                        OUTPUT INSERTED.ReportId
                        VALUES (@RFCName, @TableName, @StartTime, @EndTime, @Duration, @Status, @Remarks)";
                    using (SqlCommand command = new SqlCommand(insertMasterLogQuery, connection))
                    {
                        command.Parameters.AddWithValue("@RFCName", rfcName);
                        command.Parameters.AddWithValue("@TableName", tableName);
                        command.Parameters.AddWithValue("@StartTime", MasterTiming.StartTime);
                        command.Parameters.AddWithValue("@EndTime", MasterTiming.EndTime);
                        command.Parameters.AddWithValue("@Duration", MasterTiming.Duration);
                        command.Parameters.AddWithValue("@Status", MasterTiming.Success);
                        command.Parameters.AddWithValue("@Remarks", MasterTiming.Remarks);

                        reportId = (int)command.ExecuteScalar();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting master log: {ex.Message}");
            }
        }

        
        public static void InsertTransactionLogs(string connectionString, int reportId, string rfcName, string tableName)
        {
            if (TransactionTimings.Count == 0) return;

            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();

                    foreach (var timing in TransactionTimings)
                    {
                        string insertTransactionQuery = @"
                            INSERT INTO [dbo].[RFCReportTransactions]
                            ([ReportId], [Store Code], [StartTime], [EndTime], [Duration], [Status], [Remarks])
                            VALUES (@ReportId, @StoreCode, @StartTime, @EndTime, @Duration, @Status, @Remarks)";
                        using (SqlCommand transactionCommand = new SqlCommand(insertTransactionQuery, connection))
                        {
                            transactionCommand.Parameters.AddWithValue("@ReportId", reportId);
                            transactionCommand.Parameters.AddWithValue("@StoreCode", timing.StoreCode);
                            transactionCommand.Parameters.AddWithValue("@StartTime", timing.StartTime);
                            transactionCommand.Parameters.AddWithValue("@EndTime", timing.EndTime);
                            transactionCommand.Parameters.AddWithValue("@Duration", timing.Duration);
                            transactionCommand.Parameters.AddWithValue("@Status", timing.Success);
                            transactionCommand.Parameters.AddWithValue("@Remarks", timing.Remarks);

                            transactionCommand.ExecuteNonQuery();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TransactionStopwatch.Stop();
                Console.WriteLine($"Error inserting transaction logs: {ex.Message}");
            }
        }

      
        public static void InsertLogs(string connectionString, string rfcName, string tableName)
        {
            try
            {
                // Insert the master log and retrieve the report ID
                InsertMasterLog(connectionString, rfcName, tableName, out int reportId);

                // Only insert transaction logs if there are entries in TransactionTimings
                if (reportId > 0)
                {
                    InsertTransactionLogs(connectionString, reportId, rfcName, tableName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error inserting logs: {ex.Message}");
            }
        }

       
        private static string CurrentTime => DateTime.Now.ToString("HH:mm:ss.fff");
    }
}
