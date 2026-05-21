using System;
using System.Collections.Generic;
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
using Snowflake.Data.Client;

namespace RFC_STOCK_DATA
{

    internal class Program
    {
        static void Main(string[] args)
        {
            string RFC_MESSAGE = string.Empty;
            string sfConnStr = ConfigurationManager.ConnectionStrings["Snowflake"].ConnectionString;

            DateTime startDate = DateTime.Now.AddDays(-1);
            DateTime endDate = DateTime.Now.AddDays(-1);
            //DateTime startDate = new DateTime(2026, 04, 14);
            //DateTime endDate = new DateTime(2026, 04, 21);

            try
            {
                // 1. Fetch store codes once
                List<string> storeCodes = new List<string>();
                using (SnowflakeDbConnection sfConn = new SnowflakeDbConnection())
                {
                    sfConn.ConnectionString = sfConnStr;
                    sfConn.Open();
                    using (IDbCommand cmd = sfConn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT st_cd FROM V2RETAIL.GOLD.store_plant_master";
                        using (IDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                storeCodes.Add(reader.GetString(0));
                            }
                        }
                    }
                    sfConn.Close();
                }

                Console.WriteLine($"Fetched {storeCodes.Count} store codes.");

                // Set up SAP destination once
                RfcConfigParameters rfcPar = RFCconfing.rfcConfigparameters();
                RfcDestination dest = RfcDestinationManager.GetDestination(rfcPar);
                RfcRepository rfcrep = dest.Repository;

                for (DateTime date = endDate; date >= startDate; date = date.AddDays(-1))
                {
                    string Datefrom = date.ToString("yyyyMMdd");
                    string Datefrom1 = date.ToString("yyyy-MM-dd");

                    string tempDir = Path.GetTempPath();
                    string csvFileName = $"ET_STOCK_DATA_{Datefrom}.csv";
                    string csvFilePath = Path.Combine(tempDir, csvFileName);

                    // Track column metadata from first successful store
                    string colNames = null;
                    string colPositions = null;
                    long totalRows = 0;

                    // 2. Stream each store's rows into one CSV file
                    using (StreamWriter sw = new StreamWriter(csvFilePath, false, Encoding.UTF8))
                    {
                        foreach (string storecode in storeCodes)
                        {
                            try
                            {
                                IRfcFunction myfun = rfcrep.CreateFunction("ZPBI_MB5B_REPORT");
                                myfun.SetValue("IM_BUDAT", Datefrom);

                                IRfcTable IrfTable1 = myfun.GetTable("IT_WERKS");
                                IrfTable1.Append();
                                IrfTable1.SetValue("SIGN", "I");
                                IrfTable1.SetValue("OPTION", "CP");
                                IrfTable1.SetValue("LOW", storecode);
                                IrfTable1.SetValue("HIGH", "");

                                myfun.Invoke(dest);

                                IRfcTable IrfTable = myfun.GetTable("ET_STOCK_DATA");
                                if (IrfTable.RowCount <= 0)
                                    continue;

                                // Convert this store's data to a DataTable
                                DataTable dt = LinqHelper.ToDataTable(IrfTable, "ET_STOCK_DATA", date);

                                // Capture column metadata from first successful store
                                if (colNames == null)
                                {
                                    colNames = string.Join(", ", dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
                                    colPositions = string.Join(", ", Enumerable.Range(1, dt.Columns.Count).Select(i => $"${i}"));
                                }

                                // Write rows to CSV immediately, then release memory
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

                                totalRows += dt.Rows.Count;
                                dt.Dispose();

                                Console.WriteLine($"  Store {storecode}: {IrfTable.RowCount} rows (running total: {totalRows})");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"  Store {storecode} failed: {ex.Message}");
                            }
                        }
                    }

                    // 3. Single DELETE + PUT + COPY for the entire date
                    if (totalRows > 0 && colNames != null)
                    {
                        using (SnowflakeDbConnection sfCon = new SnowflakeDbConnection())
                        {
                            sfCon.ConnectionString = sfConnStr;
                            sfCon.Open();

                            // One DELETE for the whole date
                            using (IDbCommand delCmd = sfCon.CreateCommand())
                            {
                                delCmd.CommandText = $"DELETE FROM ET_STOCK_DATA WHERE stock_Date = '{Datefrom1}'";
                                delCmd.ExecuteNonQuery();
                            }

                            // One PUT
                            string putPath = csvFilePath.Replace("\\", "/");
                            using (IDbCommand putCmd = sfCon.CreateCommand())
                            {
                                putCmd.CommandText = $"PUT 'file://{putPath}' @%ET_STOCK_DATA AUTO_COMPRESS=TRUE OVERWRITE=TRUE";
                                putCmd.ExecuteNonQuery();
                            }

                            // One COPY INTO
                            using (IDbCommand copyCmd = sfCon.CreateCommand())
                            {
                                copyCmd.CommandText =
                                    $"COPY INTO ET_STOCK_DATA ({colNames}) " +
                                    $"FROM (SELECT {colPositions} FROM @%ET_STOCK_DATA/{csvFileName}.gz) " +
                                    "FILE_FORMAT=(TYPE='CSV' FIELD_OPTIONALLY_ENCLOSED_BY='\"' " +
                                    "NULL_IF=('') EMPTY_FIELD_AS_NULL=TRUE) PURGE=TRUE";
                                copyCmd.ExecuteNonQuery();
                            }

                            sfCon.Close();
                        }

                        Console.WriteLine($"Loaded {totalRows} rows into Snowflake for {Datefrom1}");
                    }
                    else
                    {
                        Console.WriteLine($"No data for {Datefrom1}, skipping Snowflake load.");
                    }

                    // Clean up temp CSV
                    if (File.Exists(csvFilePath))
                        File.Delete(csvFilePath);
                }

                RFC_MESSAGE = "Successfully Inserted";
            }
            catch (Exception ex)
            {
                RFC_MESSAGE = ex.Message;
                Console.WriteLine($"Error during insertion: {ex.Message}");
            }
        }

        public static bool TableHasRows(string connectionString)
        {
            using (SnowflakeDbConnection connection = new SnowflakeDbConnection())
            {
                connection.ConnectionString = connectionString;
                connection.Open();
                string query = "SELECT COUNT(1) FROM ET_STOCK_DATA WHERE CAST(stock_Date AS DATE) = '" + DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd") + "'";
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
