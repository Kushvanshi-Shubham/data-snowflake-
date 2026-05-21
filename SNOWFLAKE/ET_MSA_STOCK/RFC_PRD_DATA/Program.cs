using System;
using System.Collections.Generic;
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
using System.Runtime.InteropServices.ComTypes;
using Snowflake.Data.Client;

namespace RFC_PRD_DATA
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
            
            string Datefrom = "20241004";
            DateTime startDate = DateTime.Now.AddDays(-1);
            DateTime endDate = DateTime.Now.AddDays(-1);
            //DateTime startDate = DateTime.Now;
            //DateTime endDate = DateTime.Now;
            //DateTime startDate = new DateTime(2026, 01, 09); // Example: October 1st, 2024
            //DateTime endDate = new DateTime(2026, 01, 09);   // Example: October 5th, 2024

            try
            {
                /* ########## Log Tracker #### */
                RFC_NAME = "ZLX03N_BCG01_RFC";
                SAP_TABLE = "et_msa_stock";
                G_RFC_START_TIME = DateTime.Now;
                RFC_MESSAGE = "Success";
                IsSuccess = true;
                DATA_PULLFOR_DATE = DateTime.Now.AddDays(-1);

                RfcConfigParameters rfcPar = null;

                rfcPar = RFCconfing.rfcConfigparameters();
                RfcDestination dest = RfcDestinationManager.GetDestination(rfcPar);
                // Get RfcTable from SAP
                RfcRepository rfcrep = dest.Repository;

                string sfConnStr = ConfigurationManager.ConnectionStrings["Snowflake"].ConnectionString;
                bool tableCreated = false;


                    RFC_START_TIME = DateTime.Now;
                    RFC_MESSAGE = "Success";
                    IsSuccess = true;

                    for (DateTime date = endDate; date >= startDate; date = date.AddDays(-1))
                    {

                        Datefrom = date.ToString("yyyyMMdd");
                        string Datefrom1 = date.ToString("yyyy-MM-dd");
                        try
                        {

                            IRfcFunction myfun = null;
                            myfun = rfcrep.CreateFunction("ZLX03N_BCG01_RFC"); //RfcFunctionName
                            myfun.SetValue("S_COMP", "X");
                            myfun.SetValue("S_DATE", Datefrom);
                            myfun.Invoke(dest);
                            IRfcTable IrfTable = myfun.GetTable("ET_DATA");

                            RFC_START_TIME = DateTime.Now;

                            RFC_END_TIME = DateTime.Now;

                            rows = IrfTable.RowCount;
                           if(rows <= 0)
                            {
                                continue;
                            }
                            RFC_PULL_Records = rows;

                            //Convert RfcTable to DataTable
                            string tablename = "et_msa_stock";
                            DataTable dt = new DataTable();
                            dt = LinqHelper.ToDataTable(IrfTable, tablename, date);

                            RFC_MESSAGE = "Success";
                            RFC_PUSH_Records = dt.Rows.Count;
                            SQL_PULL_Records = RFC_PUSH_Records;

                            // Write DataTable to CSV for Snowflake PUT
                            string tempDir = Path.GetTempPath();
                            string csvFileName = $"et_msa_stock_{Datefrom}.csv";
                            string csvFilePath = Path.Combine(tempDir, csvFileName);

                            string colNames = string.Join(", ", dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
                            string colPositions = string.Join(", ", Enumerable.Range(1, dt.Columns.Count).Select(i => $"${i}"));

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

                            // Load into Snowflake: DELETE + PUT + COPY INTO
                            SQL_START_TIME = DateTime.Now;
                            SQL_PUSH_Records = 0;

                            using (SnowflakeDbConnection sfCon = new SnowflakeDbConnection())
                            {
                                sfCon.ConnectionString = sfConnStr;
                                sfCon.Open();

                                // Drop and recreate table on first load
                                if (!tableCreated)
                                {
                                    string createSql = LinqHelper.GetSnowflakeCreateTableSql(dt);
                                    using (IDbCommand dropCmd = sfCon.CreateCommand())
                                    {
                                        dropCmd.CommandText = "DROP TABLE IF EXISTS et_msa_stock";
                                        dropCmd.ExecuteNonQuery();
                                    }
                                    using (IDbCommand createCmd = sfCon.CreateCommand())
                                    {
                                        createCmd.CommandText = createSql;
                                        createCmd.ExecuteNonQuery();
                                    }
                                    tableCreated = true;
                                }

                                // DELETE existing data for the date
                                using (IDbCommand delCmd = sfCon.CreateCommand())
                                {
                                    delCmd.CommandText = $"DELETE FROM et_msa_stock WHERE MSA_STOCK_DATE = '{Datefrom1}'";
                                    delCmd.ExecuteNonQuery();
                                }

                                // PUT file to Snowflake internal stage
                                string putPath = csvFilePath.Replace("\\", "/");
                                using (IDbCommand putCmd = sfCon.CreateCommand())
                                {
                                    putCmd.CommandText = $"PUT 'file://{putPath}' @%et_msa_stock AUTO_COMPRESS=TRUE OVERWRITE=TRUE";
                                    putCmd.ExecuteNonQuery();
                                }

                                // COPY INTO table from staged file
                                using (IDbCommand copyCmd = sfCon.CreateCommand())
                                {
                                    copyCmd.CommandText =
                                        $"COPY INTO et_msa_stock ({colNames}) " +
                                        $"FROM (SELECT {colPositions} FROM @%et_msa_stock/{csvFileName}.gz) " +
                                        "FILE_FORMAT=(TYPE='CSV' FIELD_OPTIONALLY_ENCLOSED_BY='\"' " +
                                        "NULL_IF=('') EMPTY_FIELD_AS_NULL=TRUE) PURGE=TRUE";
                                    copyCmd.ExecuteNonQuery();
                                }

                                sfCon.Close();
                            }

                            // Clean up temp CSV
                            if (File.Exists(csvFilePath))
                                File.Delete(csvFilePath);

                            SQL_PUSH_Records = SQL_PULL_Records;
                            SQL_END_TIME = DateTime.Now;
                            SQL_MESSAGE = "Success";
                            IsSuccess = true;

                            Console.WriteLine($"Loaded {dt.Rows.Count} rows into Snowflake for {Datefrom1}");

                        }
                        catch (Exception ex)
                        {
                            SQL_PUSH_Records = 0;
                            SQL_END_TIME = DateTime.Now;
                            SQL_MESSAGE = ex.Message.ToString();
                            IsSuccess = false;
                            Console.WriteLine(ex.Message.ToString());
                        }
                        finally
                        {
                            
                        }
                    }
            }
            catch (Exception ex)
            {
                RFC_MESSAGE=ex.Message.ToString();
                IsSuccess=false;
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
                        break;
                }

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

            adoTable.Columns.Add("MSA_STOCK_DATE", typeof(DateTime)).DefaultValue = stock_date.ToString("yyyy-MM-dd");
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

        public static string GetSnowflakeCreateTableSql(DataTable table)
        {
            StringBuilder sql = new StringBuilder();
            sql.AppendFormat("CREATE TABLE IF NOT EXISTS {0} (", table.TableName);

            for (int i = 0; i < table.Columns.Count; i++)
            {
                sql.AppendFormat("\n\t{0}", table.Columns[i].ColumnName);

                switch (table.Columns[i].DataType.ToString().ToUpper())
                {
                    case "SYSTEM.INT16":
                        sql.Append(" SMALLINT");
                        break;
                    case "SYSTEM.INT32":
                        sql.Append(" INTEGER");
                        break;
                    case "SYSTEM.INT64":
                        sql.Append(" BIGINT");
                        break;
                    case "SYSTEM.DATETIME":
                        sql.Append(" DATE");
                        break;
                    case "SYSTEM.SINGLE":
                    case "SYSTEM.DOUBLE":
                        sql.Append(" FLOAT");
                        break;
                    case "SYSTEM.DECIMAL":
                        sql.Append(" NUMBER(18, 6)");
                        break;
                    case "SYSTEM.STRING":
                    default:
                        sql.Append(" VARCHAR");
                        break;
                }

                if (i < table.Columns.Count - 1)
                    sql.Append(",");
            }

            sql.Append("\n)");
            return sql.ToString();
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
