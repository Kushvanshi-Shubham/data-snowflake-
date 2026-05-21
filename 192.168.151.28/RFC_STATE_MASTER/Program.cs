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
using CommonUtil;

namespace RFC_STATE_MASTER
{
    internal class Program
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
        static void Main(string[] args)
        {
            CommonUtil.CommonUtil myObj_master = new CommonUtil.CommonUtil();
            CommonUtil.CommonUtil myObj = new CommonUtil.CommonUtil();

            //string Datefrom = DateTime.Now.AddDays(-1).ToString("yyyyMMdd");
            //string Dateto = DateTime.Now.ToString("yyyyMMdd");

            /* ########## Log Tracker #### */
            RFC_NAME = "ZPBI_STATE";
            SAP_TABLE = "ET_STATE";
            G_RFC_START_TIME = DateTime.Now;

            RFC_START_TIME = DateTime.Now;
            RFC_MESSAGE = "Success";
            IsSuccess = true;
            DATA_PULLFOR_DATE = DateTime.Now.AddDays(-1);

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
                    myfun = rfcrep.CreateFunction("ZPBI_STATE"); //RfcFunctionName
                    //myfun.SetValue("IM_DATE_FROM", Datefrom);
                    //myfun.SetValue("IM_DATE_TO", Dateto);
                    //IRfcTable IrfTable1 = myfun.GetTable("IT_WERKS");
                    //IRfcTable IrfTable = myfun.GetTable("ET_ARTICLE_COLOR");

                    //IrfTable1.Append();

                    //////Populate current MATNRSELECTION row with data from list
                    //IrfTable1.SetValue("SIGN", "I");
                    //IrfTable1.SetValue("OPTION", "EQ");
                    //IrfTable1.SetValue("LOW", "DH24");
                    //IrfTable1.SetValue("HIGH", "");


                    myfun.Invoke(dest);

                    IRfcTable IrfTable = myfun.GetTable("ET_STATE"); //ReturnTableName
                    rows = IrfTable.RowCount;
                    //Convert RfcTable to DataTable
                    string tablename = "ET_STATE";
                    DataTable dt = new DataTable();

                    dt = LinqHelper.ToDataTable(IrfTable, tablename, DateTime.Now.AddDays(-1));

                    RFC_END_TIME = DateTime.Now;
                    RFC_PULL_Records = rows;
                    RFC_PUSH_Records = rows;
                    RFC_MESSAGE = "Success";

                    SQL_START_TIME = DateTime.Now;
                    SQL_PULL_Records = dt.Rows.Count;

                    string consString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;

                    string tableDDL = "";

                    //tableDDL += "if not exists (select * from sys.objects where object_id = ";
                    //tableDDL += "object_id(N'[dbo].[" + tablename + "]') and type in (N'u'))";

                    //tableDDL += LinqHelper.GetCreateTableSql(dt);

                    tableDDL += "IF  EXISTS (SELECT * FROM sys.objects WHERE object_id = ";
                    tableDDL += "OBJECT_ID(N'[dbo].[" + tablename + "]') AND type in (N'U'))";
                    //tableDDL += "delete from [dbo].[" + tablename + "] where  cast(Article_Color_Date as date)=cast('" + DateTime.Now.AddDays(-1) + "' as date)";
                    tableDDL += "truncate table  [dbo].[" + tablename + "]";

                    using (SqlConnection con = new SqlConnection(consString))
                    {

                        using (SqlCommand SQLCmd = new SqlCommand(tableDDL, con))
                        {

                            con.Open();
                            //SQLCmd = new SqlCommand(tableDDL, SQLConnection);
                            SQLCmd.ExecuteNonQuery();
                            con.Close();

                        }
                        using (SqlBulkCopy sqlBulkCopy = new SqlBulkCopy(con))
                        {
                            sqlBulkCopy.DestinationTableName = tablename;
                            con.Open();
                            sqlBulkCopy.BatchSize = 100000;
                            sqlBulkCopy.BulkCopyTimeout = 0;
                            sqlBulkCopy.WriteToServer(dt);
                            con.Close();

                        }

                    }
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

                }
            }
            catch (Exception ex)
            {
                SQL_END_TIME = DateTime.Now;
                SQL_MESSAGE = ex.Message.ToString();
                SQL_PUSH_Records = 0;
                IsSuccess = false;
                RFC_MESSAGE = SQL_MESSAGE;

            }

            /*  ### lOG Details */
            myObj = new CommonUtil.CommonUtil();
            myObj.RFC_NAME = string.IsNullOrEmpty(RFC_NAME) ? "ZPBI_ARTICLE_COLOR" : RFC_NAME;
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

            /*### Log Master */
            myObj_master = new CommonUtil.CommonUtil();
            myObj_master.RFC_NAME = RFC_NAME != null ? RFC_NAME : "ZPBI_ARTICLE_COLOR";
            myObj_master.RFC_START_TIME = G_RFC_START_TIME;
            myObj_master.RFC_END_TIME = DateTime.Now;
            myObj_master.RFC_MESSAGE = string.IsNullOrEmpty(RFC_MESSAGE) ? "" : RFC_MESSAGE;
            myObj_master.SAP_TABLE = SAP_TABLE;
            myObj_master.IsSuccess = IsSuccess;
            myObj_master.logMaster();

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

            adoTable.Columns.Add("State_Date", typeof(DateTime)).DefaultValue = stock_date.ToString("yyyy-MM-dd");
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
            //var email = new MimeMessage();


            //email.From.Add(new MailboxAddress("Sender", "report@v2kart.com"));
            //email.To.Add(new MailboxAddress("Receiver Name", "report@v2kart.com"));

            //email.Subject = sub;
            //email.Body = new TextPart("html") { Text = message };
            //using (var smtp = new SmtpClient())
            //{
            //    smtp.Connect("smtp.gmail.com", 587, false);

            //    smtp.Authenticate("report@v2kart.com", "vrl@55555");
            //    smtp.Send(email);
            //    smtp.Disconnect(true);
            //}
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


