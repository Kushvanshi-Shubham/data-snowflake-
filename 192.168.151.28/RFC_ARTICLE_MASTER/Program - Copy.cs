using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using SAP.Middleware.Connector;
using System.Configuration;
using System.Diagnostics;
using CommonUtil;

namespace RFC_ARTICLE_MASTER
{
    internal class Program1
    {
        static StringBuilder sb = new StringBuilder();
        static int rows = 0, rowsfetched = 0;
        static string comments = "", endTime = "";

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

            CommonUtil.CommonUtil myObj_master = new CommonUtil.CommonUtil();
            CommonUtil.CommonUtil myObj = new CommonUtil.CommonUtil();

            var watch = Stopwatch.StartNew();

            var startTime = DateTime.Now.ToString("HH:mm:ss.fff");
            var midTime = "";
            var endTime = "";
            var midDuration = new TimeSpan();
            var endDuration = new TimeSpan();
            var remarks = "Successfully Inserted";
            bool isSuccess = true;

            /* ########## Log Tracker #### */
            RFC_NAME = "ZPBI_ART_MASTER";
            SAP_TABLE = "ET_ARTICLE";
            G_RFC_START_TIME = DateTime.Now;

            //string Datefrom = DateTime.Now.AddDays(-1).ToString("yyyyMMdd");
            //string Dateto = DateTime.Now.ToString("yyyyMMdd");

            //Define the fromDate and endDate
            DateTime fromDate = new DateTime(2022, 01, 01); // Example start date
            DateTime endDate = new DateTime(2025, 12, 24);   // Example end date

            //DateTime fromDate = DateTime.Now.AddDays(-1); // Example start date
            //DateTime endDate = DateTime.Now;   // Example end date

            string fordate = fromDate.ToString("yyyyMMdd");
            // Iterate through the range with 6-month intervals
            string consString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            using (SqlConnection con = new SqlConnection(consString))
            {
                using (SqlCommand SQLCmd = new SqlCommand("truncate table ET_ARTICLE", con))
                {

                    con.Open();
                    SQLCmd.CommandTimeout = 0;
                    //SQLCmd = new SqlCommand(tableDDL, SQLConnection);
                    SQLCmd.ExecuteNonQuery();
                    con.Close();

                }
            }
            DateTime currentDate = fromDate;
            while (currentDate <= endDate)
            {
                RFC_START_TIME = DateTime.Now;
                RFC_MESSAGE = "Success";
                IsSuccess = true;
                DATA_PULLFOR_DATE = currentDate;

                Console.WriteLine(currentDate.ToString("yyyy-MM-dd"));

                // Add 6 months to the currentDate
                currentDate = currentDate.AddDays(1);
                string forcurrentDate = currentDate.ToString("yyyyMMdd");
                Console.WriteLine(currentDate.ToString("yyyy-MM-dd"));
                try
                {
                    RfcConfigParameters rfcPar = null;

                    rfcPar = RFCconfing.rfcConfigparameters();
                    RfcDestination dest = RfcDestinationManager.GetDestination(rfcPar);
                    // Get RfcTable from SAP
                    RfcRepository rfcrep = dest.Repository;


                    try
                    {

                        IRfcFunction myfun = null;
                        myfun = rfcrep.CreateFunction("ZPBI_ART_MASTER"); //RfcFunctionName
                        myfun.SetValue("IM_DATE_FROM", fordate);
                        myfun.SetValue("IM_DATE_TO", forcurrentDate);
                        //IRfcTable IrfTable1 = myfun.GetTable("IT_WERKS");
                        //IRfcTable IrfTable = myfun.GetTable("ET_ARTICLE_COLOR");

                        //IrfTable1.Append();

                        //////Populate current MATNRSELECTION row with data from list
                        //IrfTable1.SetValue("SIGN", "I");
                        //IrfTable1.SetValue("OPTION", "EQ");
                        //IrfTable1.SetValue("LOW", "DH24");
                        //IrfTable1.SetValue("HIGH", "");


                        myfun.Invoke(dest);

                        IRfcTable IrfTable = myfun.GetTable("ET_ARTICLE"); //ReturnTableName
                        rows = IrfTable.RowCount;
                        if (rows <= 0) {
                            continue;
                        }
                        midDuration = watch.Elapsed;
                        midTime = DateTime.Now.ToString("HH:mm:ss.fff");

                        //Convert RfcTable to DataTable
                        string tablename = "ET_ARTICLE";
                        DataTable dt = new DataTable();

                        dt = LinqHelper.ToDataTable(IrfTable, tablename, fromDate);

                        RFC_END_TIME = DateTime.Now;
                        RFC_PULL_Records = rows;
                        RFC_PUSH_Records = rows;
                        RFC_MESSAGE = "Success";

                        SQL_START_TIME = DateTime.Now;
                        SQL_PULL_Records = dt.Rows.Count;


                       

                        string tableDDL = "";

                        //tableDDL += "if not exists (select * from sys.objects where object_id = ";
                        //tableDDL += "object_id(N'[dbo].[" + tablename + "]') and type in (N'u'))";

                        //tableDDL += LinqHelper.GetCreateTableSql(dt);

                        //tableDDL += "IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = ";
                        //tableDDL += "OBJECT_ID(N'[dbo].[" + tablename + "]') AND type in (N'U'))";
                        //tableDDL += "delete from [dbo].[" + tablename + "] ";


                        using (SqlConnection con = new SqlConnection(consString))
                        {

                            //using (SqlCommand SQLCmd = new SqlCommand(tableDDL, con))
                            //{

                            //    con.Open();
                            //    SQLCmd.CommandTimeout = 0;
                            //    //SQLCmd = new SqlCommand(tableDDL, SQLConnection);
                            //    SQLCmd.ExecuteNonQuery();
                            //    con.Close();

                            //}
                            using (SqlBulkCopy sqlBulkCopy = new SqlBulkCopy(con))
                            {
                                sqlBulkCopy.DestinationTableName = tablename;
                                con.Open();
                                sqlBulkCopy.BatchSize = 100000;
                                sqlBulkCopy.BulkCopyTimeout = 0;
                                sqlBulkCopy.WriteToServer(dt);
                                con.Close();

                            }
                            //using (SqlCommand SQLCmd = new SqlCommand("exec sp_merge_Article", con))
                            //{

                            //    con.Open();
                            //    SQLCmd.CommandTimeout = 0;
                            //    //SQLCmd = new SqlCommand(tableDDL, SQLConnection);
                            //    SQLCmd.ExecuteNonQuery();
                            //    con.Close();

                            //}
                            

                        }
                        
                        fordate = forcurrentDate;
                        endDuration = watch.Elapsed;
                        endTime = DateTime.Now.ToString("HH:mm:ss.fff");

                        SQL_END_TIME = DateTime.Now;
                        SQL_MESSAGE = "Success";
                        SQL_PUSH_Records = SQL_PULL_Records;

                    }
                    catch (Exception ex)
                    {
                        SQL_END_TIME = DateTime.Now;
                        SQL_MESSAGE = ex.Message.ToString();
                        SQL_PUSH_Records = 0;
                        IsSuccess = false;
                        RFC_MESSAGE = SQL_MESSAGE;

                        Console.WriteLine(ex.Message.ToString());
                        remarks = ex.Message;
                        throw new Exception(ex.Message);
                    }
                }
                catch (Exception ex)
                {
                    SQL_END_TIME = DateTime.Now;
                    SQL_MESSAGE = ex.Message.ToString();
                    SQL_PUSH_Records = 0;
                    IsSuccess = false;
                    RFC_MESSAGE = SQL_MESSAGE;

                    isSuccess = false;
                    midDuration = watch.Elapsed;
                    midTime = DateTime.Now.ToString("HH:mm:ss.fff");
                    endDuration = watch.Elapsed;
                    endTime = DateTime.Now.ToString("HH:mm:ss.fff");
                    watch.Stop();

                }
                finally
                {
                    // Insert data into the table within the finally block
                    //try
                    //{
                    //    string connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
                    //    using (var conn = new SqlConnection(connectionString))
                    //    {
                    //        conn.Open();
                    //        using (SqlCommand cmd = new SqlCommand("INSERT INTO [DataWarehouse].[dbo].[RFCReport] ([RFC Name], [Table Name], [StartTime], [SapEndTime], [SapDuration], [EndTime], [Duration], [Status], [Type],Remarks) VALUES (@RFCName, @TableName, @StartTime, @SapEndTime, @SapDuration, @EndTime, @Duration, @Status, @Type,@Remarks)", conn))
                    //        {
                    //            cmd.Parameters.AddWithValue("@RFCName", "ZPBI_ART_MASTER");
                    //            cmd.Parameters.AddWithValue("@TableName", "ET_ARTICLE");
                    //            cmd.Parameters.AddWithValue("@StartTime", startTime);
                    //            cmd.Parameters.AddWithValue("@SapEndTime", midTime);
                    //            cmd.Parameters.AddWithValue("@SapDuration", midDuration);
                    //            cmd.Parameters.AddWithValue("@EndTime", endTime);
                    //            cmd.Parameters.AddWithValue("@Duration", endDuration);
                    //            cmd.Parameters.AddWithValue("@Status", isSuccess);
                    //            cmd.Parameters.AddWithValue("@Type", "Master");
                    //            cmd.Parameters.AddWithValue("@Remarks", remarks);

                    //            // Execute the insert command
                    //            cmd.ExecuteNonQuery();
                    //            Console.WriteLine("Data inserted successfully into RFCReport table.");
                    //        }
                    //        conn.Close();
                    //        watch.Restart();
                    //    }

                    //}
                    //catch (Exception ex)
                    //{
                    //    Console.WriteLine($"Error during insertion: {ex.Message}");
                    //}
                }

                /*  ### lOG Details */
                myObj = new CommonUtil.CommonUtil();
                myObj.RFC_NAME = string.IsNullOrEmpty(RFC_NAME) ? "ZPBI_ART_MASTER" : RFC_NAME;
                myObj.DATA_PULLFOR_DATE = DATA_PULLFOR_DATE != null ? DATA_PULLFOR_DATE : DateTime.Now.AddDays(-1);
                myObj.STORE = STORE != null ? STORE : "";
                myObj.RFC_START_TIME = RFC_START_TIME != null ? RFC_START_TIME : DateTime.Now;
                myObj.RFC_END_TIME = RFC_END_TIME != null ? RFC_END_TIME : DateTime.Now;
                myObj.RFC_PULL_Records = RFC_PULL_Records;
                myObj.RFC_PUSH_Records = RFC_PUSH_Records;
                myObj.RFC_MESSAGE = string.IsNullOrEmpty(RFC_MESSAGE) ? "" : RFC_MESSAGE;
                myObj.SAP_TABLE = SAP_TABLE;
                myObj.SQL_START_TIME = SQL_START_TIME != null ? SQL_START_TIME : DateTime.Now;
                myObj.SQL_END_TIME = SQL_END_TIME != null ? SQL_END_TIME : DateTime.Now;
                myObj.SQL_PULL_Records = SQL_PULL_Records;
                myObj.SQL_PUSH_Records = SQL_PUSH_Records;
                myObj.SQL_MESSAGE = string.IsNullOrEmpty(SQL_MESSAGE) ? "" : SQL_MESSAGE;
                myObj.IsSuccess = IsSuccess;
                myObj.logDetails();

            }

            using (SqlConnection con = new SqlConnection(consString))
            {
                using (SqlCommand SQLCmd = new SqlCommand("exec [USP_SYNC_PRODUCTMASTER]", con))
                {

                    con.Open();
                    SQLCmd.CommandTimeout = 0;
                    //SQLCmd = new SqlCommand(tableDDL, SQLConnection);
                    SQLCmd.ExecuteNonQuery();
                    con.Close();

                }
            }
            /*### Log Master */
            myObj_master = new CommonUtil.CommonUtil();
            myObj_master.RFC_NAME = RFC_NAME != null ? RFC_NAME : "ZPBI_ART_MASTER";
            myObj_master.RFC_START_TIME = G_RFC_START_TIME;
            myObj_master.RFC_END_TIME = DateTime.Now;
            myObj_master.RFC_MESSAGE = string.IsNullOrEmpty(RFC_MESSAGE) ? "" : RFC_MESSAGE;
            myObj_master.SAP_TABLE = SAP_TABLE;
            myObj_master.IsSuccess = IsSuccess;
            myObj_master.logMaster();

        }



    }
}