using SAP.Middleware.Connector;
using System;
using System.Collections.Generic;
using Snowflake.Data.Client;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Configuration;

using MimeKit;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;
using System.Runtime.InteropServices.ComTypes;
using CommonUtil;

namespace RFC_Sales_Data
{
    internal class Program
    {
        
        static void Main(string[] args)
        {
           

            string Datefrom = "20241004";
            DateTime startDate = DateTime.Now.AddDays(-1);
            DateTime endDate = DateTime.Now.AddDays(-1);
            //DateTime startDate = new DateTime(2024, 01, 01); // Example: October 1st, 2024
            //DateTime endDate = new DateTime(2026, 03, 12);   // Example: October 5th, 2024


            //string Datefrom = "20241231";
            //string Dateto = "20241231";

            /* ########## Log Tracker #### */



            //while (TableHasRows(ConfigurationManager.ConnectionStrings["Snowflake"].ConnectionString))
            //{
            try
            {
                RfcConfigParameters rfcPar = null;

                rfcPar = RFCconfing.rfcConfigparameters();
                RfcDestination dest = RfcDestinationManager.GetDestination(rfcPar);
                // Get RfcTable from SAP
                RfcRepository rfcrep = dest.Repository;


                try
                {
                    for (DateTime date = endDate; date >= startDate; date = date.AddDays(-1))
                    {

                        Datefrom=date.ToString("yyyyMMdd");
                        string Datefrom1 = date.ToString("yyyy-MM-dd");
                        IRfcFunction myfun = null;
                        myfun = rfcrep.CreateFunction("ZPBI_ART_SALES"); //RfcFunctionName
                        myfun.SetValue("IM_DATE_FROM", Datefrom);
                        myfun.SetValue("IM_DATE_TO", Datefrom);                       

                        myfun.Invoke(dest);                        

                        IRfcTable IrfTable = myfun.GetTable("ET_SALES_DATA"); //ReturnTableName
                      int  rows = IrfTable.RowCount;

                        
                        //Convert RfcTable to DataTable
                        string tablename = "ET_SALES_DATA";
                        DataTable dt = new DataTable();

                        dt = LinqHelper.ToDataTable(IrfTable, tablename, date);

                       


                        // Write DataTable to temp CSV for Snowflake bulk load
                        string tempDir = Path.GetTempPath();
                        string csvFileName = $"ET_SALES_DATA_{Datefrom}.csv";
                        string csvFilePath = Path.Combine(tempDir, csvFileName);

                        using (StreamWriter sw = new StreamWriter(csvFilePath, false, Encoding.UTF8))
                        {
                            foreach (DataRow dataRow in dt.Rows)
                            {
                                var fields = new List<string>();
                                foreach (DataColumn col in dt.Columns)
                                {
                                    object val = dataRow[col];
                                    if (val == null || val == DBNull.Value)
                                        fields.Add("");
                                    else if (val is string strVal)
                                        fields.Add($"\"{strVal.Replace("\"", "\"\"")}\"");
                                    else if (val is DateTime dtVal)
                                        fields.Add($"\"{dtVal:yyyy-MM-dd}\"");
                                    else if (val is decimal || val is double || val is float)
                                        fields.Add(Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture));
                                    else
                                        fields.Add(val.ToString());
                                }
                                sw.WriteLine(string.Join(",", fields));
                            }
                        }

                        string sfConnStr = ConfigurationManager.ConnectionStrings["Snowflake"].ConnectionString;
                        using (SnowflakeDbConnection sfCon = new SnowflakeDbConnection())
                        {
                            sfCon.ConnectionString = sfConnStr;
                            sfCon.Open();

                            // Delete existing records for this date
                            using (IDbCommand delCmd = sfCon.CreateCommand())
                            {
                                delCmd.CommandText = $"DELETE FROM ET_SALES_DATA WHERE Sales_Date = '{Datefrom1}'";
                                delCmd.ExecuteNonQuery();
                            }

                            // PUT CSV to Snowflake internal table stage
                            string putPath = csvFilePath.Replace("\\", "/");
                            using (IDbCommand putCmd = sfCon.CreateCommand())
                            {
                                putCmd.CommandText = $"PUT 'file://{putPath}' @%ET_SALES_DATA AUTO_COMPRESS=TRUE OVERWRITE=TRUE";
                                putCmd.ExecuteNonQuery();
                            }

                            // Build column list dynamically from DataTable
                            string colNames = string.Join(", ", dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
                            string colPositions = string.Join(", ", Enumerable.Range(1, dt.Columns.Count).Select(i => $"${i}"));

                            // COPY INTO Snowflake table from stage
                            using (IDbCommand copyCmd = sfCon.CreateCommand())
                            {
                                copyCmd.CommandText =
                                    $"COPY INTO ET_SALES_DATA ({colNames}) " +
                                    $"FROM (SELECT {colPositions} FROM @%ET_SALES_DATA/{csvFileName}.gz) " +
                                    "FILE_FORMAT=(TYPE='CSV' FIELD_OPTIONALLY_ENCLOSED_BY='\"' " +
                                    "NULL_IF=('') EMPTY_FIELD_AS_NULL=TRUE) PURGE=TRUE";
                                copyCmd.ExecuteNonQuery();
                            }

                            sfCon.Close();
                        }

                        // Clean up temp CSV
                        if (File.Exists(csvFilePath))
                            File.Delete(csvFilePath);

                        Console.WriteLine("Bulk copy to Snowflake completed successfully! " + Datefrom);
                      
                    }

                }
                catch (Exception ex)
                {
                    

                    Console.WriteLine(ex.Message.ToString()+"xyz:-"+Datefrom);

                }

            }
            catch (Exception ex)
            {

              
            }
          


        }
        public static bool TableHasRows(string connectionString)
        {
            using (SnowflakeDbConnection connection = new SnowflakeDbConnection())
            {
                connection.ConnectionString = connectionString;
                connection.Open();
                string query = "SELECT COUNT(1) FROM ET_SALES_DATA WHERE CAST(Sales_Date AS DATE) = '" + DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd") + "'";
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = query;
                    int rowCount = Convert.ToInt32(command.ExecuteScalar());
                    return rowCount <= 0;
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

            adoTable.Columns.Add("Sales_Date", typeof(DateTime)).DefaultValue = stock_date.ToString("yyyy-MM-dd");
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
    public static class sendMail
    {
        public static void send(string sub, string message)
        {
            var email = new MimeMessage();


            email.From.Add(new MailboxAddress("Sender", "report@v2kart.com"));
            email.To.Add(new MailboxAddress("Receiver Name", "report@v2kart.com"));

            email.Subject = sub;
            email.Body = new TextPart("html") { Text = message };
            using (var smtp = new SmtpClient())
            {
                smtp.Connect("smtp.gmail.com", 587, false);

                smtp.Authenticate("report@v2kart.com", "vrl@55555");
                smtp.Send(email);
                smtp.Disconnect(true);
            }
        }
    }
    public static class RFCconfing
    {
        public static RfcConfigParameters rfcConfigparameters()
        {
            string settingValue = ConfigurationManager.AppSettings["Name"];
            RfcConfigParameters rfcPar = null;
            try
            {
                rfcPar = new RfcConfigParameters();
            }
            catch { }

            rfcPar.Add(RfcConfigParameters.Name, ConfigurationManager.AppSettings["Name"]);
            rfcPar.Add(RfcConfigParameters.AppServerHost, ConfigurationManager.AppSettings["AppServerHost"]);
            rfcPar.Add(RfcConfigParameters.Client, ConfigurationManager.AppSettings["Client"]);

            rfcPar.Add(RfcConfigParameters.User, ConfigurationManager.AppSettings["User"]);
            rfcPar.Add(RfcConfigParameters.Password, ConfigurationManager.AppSettings["Password"]);

            rfcPar.Add(RfcConfigParameters.SystemNumber, ConfigurationManager.AppSettings["SystemNumber"]);



            rfcPar.Add(RfcConfigParameters.Language, ConfigurationManager.AppSettings["Language"]);


            return rfcPar;
        }
    }
}
