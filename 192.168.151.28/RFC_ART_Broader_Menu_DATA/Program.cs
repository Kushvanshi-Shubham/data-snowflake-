using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using SAP.Middleware.Connector;
using System.Configuration;
using System.Diagnostics;
using CommonUtil;
//using RFCReportUtils;

namespace RFC_STOCK_DATA
{

    internal class Program
    {
        static void Main(string[] args)
        {
            ForMultipleStores.StartMasterProcess();

            CommonUtil.CommonUtil myObj = new CommonUtil.CommonUtil();
            CommonUtil.CommonUtil myObj_master = new CommonUtil.CommonUtil();

            myObj_master.RFC_START_TIME = DateTime.Now;
            myObj_master.RFC_NAME = "ZPBI_ART_MASTER_CHAR_VAL";
            myObj_master.SAP_TABLE = "ET_ART_Broader_Menu_DATA";
            string RFC_MESSAGE = string.Empty;
            string Datefrom = "20241004";
            // Define the start and end dates
            DateTime startDate = DateTime.Now.AddDays(-1); // Start date (7 days ago)
            DateTime endDate = DateTime.Now.AddDays(-1);   // End date (yesterday)
            //DateTime startDate = new DateTime(2025, 06, 09); // Example: October 1st, 2024
            //DateTime endDate = new DateTime(2025, 06, 09);   // Example: October 5th, 2024

            string connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"]?.ConnectionString
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
            try
            {


                for (DateTime date = endDate; date >= startDate; date = date.AddDays(-1))
                {
                    // Get the last day of the current month
                   // DateTime endOfMonth = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
                    //.AddHours(23)
                    //.AddMinutes(59)
                    //.AddSeconds(59);

                    // Print EOD of the month if within range
                    //if (endOfMonth <= date)
                    //{
                        //Console.WriteLine($"EOD of {endOfMonth:yyyy-MM-dd HH:mm:ss}");


                        Datefrom = date.ToString("yyyyMMdd");
                        
                        // Define the connection string (replace with your actual connection string)
                        string consString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

                        // Query to get the storecode from your table
                        string query = "select MATNR from ET_ARTICLE";

                        using (SqlConnection conn = new SqlConnection(consString))
                        {
                            conn.Open();

                            // Step 1: Retrieve storecodes
                            //SqlCommand cmd = new SqlCommand(query, conn);
                            //SqlDataReader reader = cmd.ExecuteReader();

                            // Step 2: Loop through each storecode
                            //while (TableHasRows(consString))
                            //{
                            //while (reader.Read())
                            //{
                                //string storecode = reader["MATNR"].ToString();

                                var startTIme = new Stopwatch();
                                //var localStartTime = ForMultipleStores.CurrentTime;
                                //var remarks = "Successfully Inserted";
                                //bool isSuccess = true;
                                //var midTime = "";
                                //var endTime = "";
                                //var midDuration = new TimeSpan();
                                //var endDuration = new TimeSpan();
                                ForMultipleStores.StartTransactionProcess();

                            myObj = new CommonUtil.CommonUtil();
                            myObj.DATA_PULLFOR_DATE = endDate;
                            //myObj.STORE = storecode;


                            try
                                {

                                    startTIme.Restart();

                                    RfcConfigParameters rfcPar = null;

                                    rfcPar = RFCconfing.rfcConfigparameters();
                                    RfcDestination dest = RfcDestinationManager.GetDestination(rfcPar);
                                    // Get RfcTable from SAP
                                    RfcRepository rfcrep = dest.Repository;

                                    IRfcFunction myfun = null;
                                    myfun = rfcrep.CreateFunction("ZPBI_ART_MASTER_CHAR_VAL"); //RfcFunctionName

                                //myfun.SetValue("IM_DATE_FROM", Datefrom);
                                //myfun.SetValue("IM_DATE_TO", Datefrom);
                                


                                //IRfcTable IrfTable1 = myfun.GetTable("IT_MATNR");
                                IRfcTable IrfTable = myfun.GetTable("ET_DATA");

                                myObj.SAP_TABLE = "ET_DATA";
                                //List<ITwerks> twerks = new List<ITwerks>();
                                //Add select option values to MATNRSELECTION table
                                //foreach (List< ITwerks> lvi in k)
                                //{
                                //Create new MATNRSELECTION row                               

                                //IrfTable1.Append();

                                ////Populate current MATNRSELECTION row with data from list
                                //IrfTable1.SetValue("SIGN", "I");
                                //IrfTable1.SetValue("OPTION", "CP");
                                //IrfTable1.SetValue("LOW", storecode);
                                //IrfTable1.SetValue("HIGH", "");

                                myObj.RFC_NAME = "ZPBI_ART_MASTER_CHAR_VAL";
                                myObj.RFC_START_TIME = DateTime.Now;
                                myfun.Invoke(dest);
                                myObj.RFC_END_TIME = DateTime.Now;
                                //IRfcTable IrfTable = myfun.GetTable("ET_STOCK_DATA"); //ReturnTableName
                                int rows = IrfTable.RowCount;
                                if(rows <= 0)
                                {
                                    continue;
                                }
                                myObj.RFC_PULL_Records = rows;
                                //here we have to calculate midtime 
                                startTIme.Stop();

                                    //midDuration = ForMultipleStores.CurrentStoreDurationElapsed;
                                    //midTime = ForMultipleStores.CurrentTime;
                                    //Convert RfcTable to DataTable
                                    string tablename = "ET_ART_Broader_Menu_DATA";

                                

                                    DataTable dt = new DataTable();

                                    dt = LinqHelper.ToDataTable(IrfTable, tablename, date);
                                myObj.RFC_PUSH_Records = dt.Rows.Count;                                
                                myObj.RFC_MESSAGE = "Success";                                

                                //comments = "Successful";
                                //log.LogWrite("RFC table to Datatable");
                                string tableDDL = "";

                                //tableDDL += "if not exists (select * from sys.objects where object_id = ";
                                //tableDDL += "object_id(N'[dbo].[" + tablename + "]') and type in (N'u'))";

                                //tableDDL += LinqHelper.GetCreateTableSql(dt);

                                tableDDL += "IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = ";
                                tableDDL += "OBJECT_ID(N'[dbo].[" + tablename + "]') AND type in (N'U'))";
                                tableDDL += "truncate table  [dbo].[" + tablename + "]";
                                myObj.SQL_START_TIME = DateTime.Now;
                                using (SqlConnection con = new SqlConnection(consString))
                                    {

                                    using (SqlCommand SQLCmd = new SqlCommand(tableDDL, con))
                                    {

                                        con.Open();
                                        SQLCmd.CommandTimeout = 0;
                                        //SQLCmd = new SqlCommand(tableDDL, SQLConnection);
                                        SQLCmd.ExecuteNonQuery();
                                        con.Close();
                                        // log.LogWrite("Drop/Delete SQL Run");
                                    }
                                    myObj.SQL_PULL_Records = dt.Rows.Count;
                                    using (SqlBulkCopy sqlBulkCopy = new SqlBulkCopy(con))
                                        {
                                            sqlBulkCopy.DestinationTableName = tablename;
                                            con.Open();
                                            sqlBulkCopy.BatchSize = 100000;
                                            sqlBulkCopy.BulkCopyTimeout = 0;
                                        sqlBulkCopy.WriteToServer(dt);
                                        con.Close();
                                        myObj.SQL_END_TIME=DateTime.Now;
                                        myObj.SQL_PUSH_Records=dt.Rows.Count;
                                        myObj.SQL_MESSAGE = "Success";
                                        myObj.IsSuccess = true;
                                        //rowsfetched = dt.Rows.Count;
                                        //endTime = DateTime.Now.ToString();
                                        //log.LogWrite("Data inserted Successfully ");
                                    }

                                    }
                                    //endDuration = ForMultipleStores.CurrentStoreDurationElapsed;
                                    //endTime = ForMultipleStores.CurrentTime;
                                    //ForMultipleStores.LogTransactionTiming(startTIme.ToString(),storecode, true, "Successfully Inserted"); ;
                                }
                                catch (Exception ex)
                                {
                                myObj.SQL_MESSAGE= ex.ToString();
                                myObj.IsSuccess = false;
                                    //remarks = ex.Message;
                                    //isSuccess = false;
                                    //midDuration = ForMultipleStores.CurrentStoreDurationElapsed;
                                    //midTime = ForMultipleStores.CurrentTime;
                                    //endDuration = ForMultipleStores.CurrentStoreDurationElapsed;
                                    //endTime = ForMultipleStores.CurrentTime;
                                    //ForMultipleStores.MakeProgramAsFail();

                                    //log.LogWrite("Error : " + ex.Message.ToString());
                                    //Console.WriteLine(ex.Message.ToString());
                                    //endTime = DateTime.Now.ToString();
                                    //rows = -1;
                                    //rowsfetched = -1;
                                    //comments = ex.Message;
                                    //ForMultipleStores.LogTransactionTiming(startTIme.ToString(),storecode, false, ex.Message);
                                }
                            myObj.logDetails();

                            //}
                            //}


                        }

                    //}
                }
                ForMultipleStores.StopMasterProcess(true, "Successfully Inserted");
                RFC_MESSAGE = "Successfully Inserted";
                myObj_master.IsSuccess = true;

            }
            catch (Exception ex)
            {
                myObj_master.IsSuccess = false;
                RFC_MESSAGE = ex.Message;
                ForMultipleStores.StopMasterProcess(false, ex.Message);
                Console.WriteLine($"Error during insertion: {ex.Message}");
            }
            finally
            {
                try
                {
                    ForMultipleStores.InsertLogs(connectionString, "ZPBI_MB5B_REPORT", "ET_STOCK_DATA");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during final log insertion: {ex.Message}");
                }
                myObj_master.RFC_END_TIME = DateTime.Now;
                myObj_master.RFC_MESSAGE = RFC_MESSAGE;
                myObj_master.logMaster();
            }
        }

        public static bool TableHasRows(string connectionString)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT COUNT(1) FROM [ET_STOCK_DATA] where cast([stock_Date] as date) = '" + DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd") + "'";
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    int rowCount = (int)command.ExecuteScalar();
                    return rowCount > 0 ? false : true;
                }
            }
        }
    }

    public static class LinqHelper
    {
        public static string GetCreateTableSql(DataTable table)
        {
            StringBuilder sql = new StringBuilder();
            StringBuilder alterSql = new StringBuilder();

            sql.AppendFormat("CREATE TABLE [{0}] (", table.TableName);

            for (int i = 0; i < table.Columns.Count; i++)
            {
                bool isNumeric = false;
                bool usesColumnDefault = true;

                sql.AppendFormat("\n\t[{0}]", table.Columns[i].ColumnName);

                switch (table.Columns[i].DataType.ToString().ToUpper())
                {
                    case "SYSTEM.INT16":
                        sql.Append(" smallint");
                        isNumeric = true;
                        break;
                    case "SYSTEM.INT32":
                        sql.Append(" int");
                        isNumeric = true;
                        break;
                    case "SYSTEM.INT64":
                        sql.Append(" bigint");
                        isNumeric = true;
                        break;
                    case "SYSTEM.DATETIME":
                        sql.Append(" datetime");
                        usesColumnDefault = false;
                        break;
                    case "SYSTEM.STRING":
                        sql.AppendFormat(" nvarchar(max)");
                        //sql.AppendFormat(" nvarchar({0})", table.Columns[i].MaxLength);
                        break;
                    case "SYSTEM.SINGLE":
                        sql.Append(" single");
                        isNumeric = true;
                        break;
                    case "SYSTEM.DOUBLE":
                        sql.Append(" double");
                        isNumeric = true;
                        break;
                    case "SYSTEM.DECIMAL":
                        sql.AppendFormat(" decimal(18, 6)");
                        isNumeric = true;
                        break;
                    default:
                        sql.AppendFormat(" nvarchar(max)");
                        //sql.AppendFormat(" nvarchar({0})", table.Columns[i].MaxLength);
                        break;
                }

                //if (table.Columns[i].AutoIncrement)
                //{
                //    sql.AppendFormat(" IDENTITY({0},{1})",
                //        table.Columns[i].AutoIncrementSeed,
                //        table.Columns[i].AutoIncrementStep);
                //}
                //else
                //{
                //    // DataColumns will add a blank DefaultValue for any AutoIncrement column. 
                //    // We only want to create an ALTER statement for those columns that are not set to AutoIncrement. 
                //    if (table.Columns[i].DefaultValue != null)
                //    {
                //        if (usesColumnDefault)
                //        {
                //            if (isNumeric)
                //            {
                //                alterSql.AppendFormat("\nALTER TABLE {0} ADD CONSTRAINT [DF_{0}_{1}]  DEFAULT ({2}) FOR [{1}];",
                //                    table.TableName,
                //                    table.Columns[i].ColumnName,
                //                    0);
                //            }
                //            else
                //            {
                //                alterSql.AppendFormat("\nALTER TABLE {0} ADD CONSTRAINT [DF_{0}_{1}]  DEFAULT ('{2}') FOR [{1}];",
                //                    table.TableName,
                //                    table.Columns[i].ColumnName,
                //                    "");
                //            }
                //        }
                //        else
                //        {
                //            // Default values on Date columns, e.g., "DateTime.Now" will not translate to SQL.
                //            // This inspects the caption for a simple XML string to see if there is a SQL compliant default value, e.g., "GETDATE()".
                //            try
                //            {
                //                System.Xml.XmlDocument xml = new System.Xml.XmlDocument();

                //                xml.LoadXml(table.Columns[i].Caption);

                //                alterSql.AppendFormat("\nALTER TABLE {0} ADD CONSTRAINT [DF_{0}_{1}]  DEFAULT ({2}) FOR [{1}];",
                //                    table.TableName,
                //                    table.Columns[i].ColumnName,
                //                    xml.GetElementsByTagName("defaultValue")[0].InnerText);
                //            }
                //            catch
                //            {
                //                // Handle
                //            }
                //        }
                //    }
                //}

                //if (!table.Columns[i].AllowDBNull)
                //{
                //    sql.Append(" NOT NULL");
                //}

                sql.Append(",");
            }

            if (table.PrimaryKey.Length > 0)
            {
                StringBuilder primaryKeySql = new StringBuilder();

                primaryKeySql.AppendFormat("\n\tCONSTRAINT PK_{0} PRIMARY KEY (", table.TableName);

                for (int i = 0; i < table.PrimaryKey.Length; i++)
                {
                    primaryKeySql.AppendFormat("{0},", table.PrimaryKey[i].ColumnName);
                }

                primaryKeySql.Remove(primaryKeySql.Length - 1, 1);
                primaryKeySql.Append(")");

                sql.Append(primaryKeySql);
            }
            else
            {
                sql.Remove(sql.Length - 1, 1);
            }

            sql.AppendFormat("\n);\n{0}", alterSql.ToString());

            return sql.ToString();
        }
        public static DataTable ToDataTable(this IRfcTable sapTable, string name, DateTime stock_date)
        {
            DataTable adoTable = new DataTable(name);
            //... Create ADO.Net table.  
            for (int liElement = 0; liElement < sapTable.ElementCount; liElement++)
            {
                RfcElementMetadata metadata = sapTable.GetElementMetadata(liElement);
                adoTable.Columns.Add(metadata.Name, GetDataType(metadata.DataType));
            }

            adoTable.Columns.Add("stock_Date", typeof(DateTime)).DefaultValue = stock_date.ToString("yyyy-MM-dd");
            //Transfer rows from SAP Table ADO.Net table.  
            foreach (IRfcStructure row in sapTable)
            {
                DataRow ldr = adoTable.NewRow();
                for (int liElement = 0; liElement < sapTable.ElementCount; liElement++)
                {
                    RfcElementMetadata metadata = sapTable.GetElementMetadata(liElement);

                    switch (metadata.DataType)
                    {
                        case RfcDataType.DATE:
                            ldr[metadata.Name] = row.GetString(metadata.Name).Substring(0, 4) + row.GetString(metadata.Name).Substring(5, 2) + row.GetString(metadata.Name).Substring(8, 2);
                            break;
                        case RfcDataType.BCD:
                            ldr[metadata.Name] = row.GetDecimal(metadata.Name);
                            break;
                        case RfcDataType.CHAR:
                            ldr[metadata.Name] = row.GetString(metadata.Name);
                            break;
                        case RfcDataType.STRING:
                            ldr[metadata.Name] = row.GetString(metadata.Name);
                            break;
                        case RfcDataType.INT2:
                            ldr[metadata.Name] = row.GetInt(metadata.Name);
                            break;
                        case RfcDataType.INT4:
                            ldr[metadata.Name] = row.GetInt(metadata.Name);
                            break;
                        case RfcDataType.FLOAT:
                            ldr[metadata.Name] = row.GetDouble(metadata.Name);
                            break;
                        default:
                            ldr[metadata.Name] = row.GetString(metadata.Name);
                            break;
                    }
                }
                adoTable.Rows.Add(ldr);

            }
            return adoTable;
        }

        public static Type GetDataType(RfcDataType rfcDataType)
        {
            switch (rfcDataType)
            {
                case RfcDataType.DATE:
                    return typeof(string);
                case RfcDataType.CHAR:
                    return typeof(string);
                case RfcDataType.STRING:
                    return typeof(string);
                case RfcDataType.BCD:
                    return typeof(decimal);
                case RfcDataType.INT2:
                    return typeof(int);
                case RfcDataType.INT4:
                    return typeof(int);
                case RfcDataType.FLOAT:
                    return typeof(double);
                default:
                    return typeof(string);
            }
        }
    }

    public class LogWriter
    {
        private string m_exePath = string.Empty;
        public LogWriter(string logMessage)
        {
            LogWrite(logMessage);
        }
        public void LogWrite(string logMessage)
        {
            m_exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            try
            {
                using (StreamWriter w = File.AppendText(m_exePath + "\\" + "log.txt"))
                {
                    Log(logMessage, w);
                }
            }
            catch (Exception ex)
            {
            }
        }

        public void Log(string logMessage, TextWriter txtWriter)
        {
            try
            {
                txtWriter.Write("\r\nLog Entry : ");
                txtWriter.WriteLine("{0} {1}", DateTime.Now.ToLongTimeString(),
                    DateTime.Now.ToLongDateString());
                txtWriter.WriteLine("  :");
                txtWriter.WriteLine("  :{0}", logMessage);
                txtWriter.WriteLine("-------------------------------");
            }
            catch (Exception ex)
            {
            }
        }
    }
}

