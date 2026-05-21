using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Net.Mail;

using MailKit.Net.Smtp;
using MailKit;
using MimeKit;

using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Vml;
using Microsoft.Office.Interop;
using Microsoft.Office.Interop.Excel;
using OfficeOpenXml;
using Snowflake.Data.Client;
using DataTable = System.Data.DataTable;
using Excel = Microsoft.Office.Interop.Excel;
using Path = System.IO.Path;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;
using CommonUtil;

namespace SYNC_STORE_PLANT_MASTER
{
    public class Program
    {

        static string filename = "", startTime = "", endTime = "", comments = "";
        static int rows=0, rowsfetched=0;
        static void Main(string[] args)
        {
            
            LogWriter log = new LogWriter("Import excel");
            try
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                string strFileName = ConfigurationManager.AppSettings["ExcelFilePath"] ?? @"\\Mis\01-mis\01-MASTER\ST-MASTER.xlsx";
                string snowflakeTableName = ConfigurationManager.AppSettings["SnowflakeTableName"] ?? "STORE_PLANT_MASTER";
                string sfConnStr = ConfigurationManager.ConnectionStrings["Snowflake"].ConnectionString;

                System.Data.DataTable tbl = new System.Data.DataTable();

                Console.WriteLine("section 1");
                
                using (ExcelPackage package = new ExcelPackage(new FileInfo(strFileName)))
                {
                    Console.WriteLine("section 2");
                    
                    int i = 0;
                    foreach (var worksheet1 in package.Workbook.Worksheets)
                    {
                        Console.WriteLine("section 3");
                        

                        filename = worksheet1.ToString();
                        startTime = DateTime.Now.ToString();
                        

                        tbl = LinqHelper.GetDataTableFromExcel(strFileName, ref rows, true, i);

                        if (tbl == null)
                        {
                            log.LogWrite($"Worksheet {i} could not be loaded from {strFileName} - skipping.");
                            Console.WriteLine($"Worksheet {i} could not be loaded - skipping.");
                            i += 1;
                            continue;
                        }

                        rowsfetched = tbl.Rows.Count;
                        comments = rows == rowsfetched ? "Successful" : "Data Missed";

                        tbl.TableName = snowflakeTableName;

                        // Write DataTable to CSV for Snowflake load
                        string tempDir = Path.GetTempPath();
                        string csvFileName = $"{snowflakeTableName}_{DateTime.Now:yyyyMMddHHmmss}.csv";
                        string csvFilePath = Path.Combine(tempDir, csvFileName);

                        using (StreamWriter sw = new StreamWriter(csvFilePath, false, Encoding.UTF8))
                        {
                            foreach (DataRow dataRow in tbl.Rows)
                            {
                                var csvFields = new List<string>();
                                foreach (DataColumn col in tbl.Columns)
                                {
                                    object val = dataRow[col];
                                    if (val == null || val == DBNull.Value)
                                        csvFields.Add("");
                                    else if (val is string strVal)
                                        csvFields.Add($"\"{strVal.Replace("\"", "\"\"")}\"");
                                    else if (val is DateTime dtVal)
                                        csvFields.Add($"\"{dtVal:yyyy-MM-dd HH:mm:ss}\"");
                                    else if (val is decimal || val is double || val is float)
                                        csvFields.Add(Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture));
                                    else
                                        csvFields.Add($"\"{val.ToString().Replace("\"", "\"\"")}\"");
                                }
                                sw.WriteLine(string.Join(",", csvFields));
                            }
                        }

                        string colNames = string.Join(", ", tbl.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\""));
                        string colPositions = string.Join(", ", Enumerable.Range(1, tbl.Columns.Count).Select(n => $"${n}"));

                        using (SnowflakeDbConnection sfCon = new SnowflakeDbConnection())
                        {
                            sfCon.ConnectionString = sfConnStr;
                            sfCon.Open();

                            using (IDbCommand dropCmd = sfCon.CreateCommand())
                            {
                                dropCmd.CommandText = $"DROP TABLE IF EXISTS {snowflakeTableName}";
                                dropCmd.ExecuteNonQuery();
                              
                                log.LogWrite("Drop SQL Run");
                            }

                            string createSql = LinqHelper.GetCreateTableSql(tbl);
                            using (IDbCommand createCmd = sfCon.CreateCommand())
                            {
                                createCmd.CommandText = createSql;
                                createCmd.ExecuteNonQuery();
                               
                                log.LogWrite("Create SQL Run");
                            }

                            string putPath = csvFilePath.Replace("\\", "/");
                            using (IDbCommand putCmd = sfCon.CreateCommand())
                            {
                                putCmd.CommandText = $"PUT 'file://{putPath}' @%{snowflakeTableName} AUTO_COMPRESS=TRUE OVERWRITE=TRUE";
                                putCmd.ExecuteNonQuery();
                            }

                            using (IDbCommand copyCmd = sfCon.CreateCommand())
                            {
                                copyCmd.CommandText =
                                    $"COPY INTO {snowflakeTableName} ({colNames}) " +
                                    $"FROM (SELECT {colPositions} FROM @%{snowflakeTableName}/{csvFileName}.gz) " +
                                    "FILE_FORMAT=(TYPE='CSV' FIELD_OPTIONALLY_ENCLOSED_BY='\"' " +
                                    "NULL_IF=('') EMPTY_FIELD_AS_NULL=TRUE) PURGE=TRUE";
                                copyCmd.ExecuteNonQuery();
                               
                            }

                            sfCon.Close();
                        }

                        endTime = DateTime.Now.ToString();
                        log.LogWrite("Data inserted Successfully ");

                        if (File.Exists(csvFilePath))
                            File.Delete(csvFilePath);

                        i += 1;
                    }
                }
            }
            catch (Exception ex)
            {
                
                log.LogWrite(ex.Message.ToString());
            }
        }


    }
    public static class LinqHelper
    {
        public static string GetCreateTableSql(DataTable table)
        {
            StringBuilder sql = new StringBuilder();

            sql.AppendFormat("CREATE TABLE {0} (", table.TableName);

            for (int i = 0; i < table.Columns.Count; i++)
            {
                sql.AppendFormat("\n\t\"{0}\"", table.Columns[i].ColumnName);

                switch (table.Columns[i].DataType.ToString().ToUpper())
                {
                    case "SYSTEM.INT16":
                        sql.Append(" SMALLINT");
                        break;
                    case "SYSTEM.INT32":
                        sql.Append(" INT");
                        break;
                    case "SYSTEM.INT64":
                        sql.Append(" BIGINT");
                        break;
                    case "SYSTEM.DATETIME":
                        sql.Append(" TIMESTAMP_NTZ");
                        break;
                    case "SYSTEM.STRING":
                        sql.Append(" VARCHAR");
                        break;
                    case "SYSTEM.SINGLE":
                        sql.Append(" FLOAT");
                        break;
                    case "SYSTEM.DOUBLE":
                        sql.Append(" DOUBLE");
                        break;
                    case "SYSTEM.DECIMAL":
                        sql.Append(" NUMBER(18, 6)");
                        break;
                    default:
                        sql.Append(" VARCHAR");
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
                    primaryKeySql.AppendFormat("\"{0}\",", table.PrimaryKey[i].ColumnName);
                }

                primaryKeySql.Remove(primaryKeySql.Length - 1, 1);
                primaryKeySql.Append(")");

                sql.Append(primaryKeySql);
            }
            else
            {
                sql.Remove(sql.Length - 1, 1);
            }

            sql.Append("\n);");

            return sql.ToString();
        }
        public static System.Data.DataTable GetDataTableFromExcel(string path,ref int rows, bool hasHeader = true, int WorksheetNumber = 0)
        {
            System.Data.DataTable tbl = null;
            LogWriter helperLog = new LogWriter("GetDataTableFromExcel start: " + path);
            try
            {
                if (!File.Exists(path))
                {
                    string msg = $"Excel file not found at path: {path}";
                    Console.WriteLine(msg);
                    helperLog.LogWrite(msg);
                    return null;
                }

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                using (var pck = new OfficeOpenXml.ExcelPackage())
                {
                    using (var stream = File.OpenRead(path))
                    {
                        pck.Load(stream);
                    }
                    var ws = pck.Workbook.Worksheets[WorksheetNumber];
                    if (ws == null || ws.Dimension == null)
                    {
                        string msg = $"Worksheet {WorksheetNumber} missing or empty in {path}";
                        Console.WriteLine(msg);
                        helperLog.LogWrite(msg);
                        return null;
                    }

                    tbl = new System.Data.DataTable(ws.Name);
                    foreach (var firstRowCell in ws.Cells[1, 1, 1, ws.Dimension.End.Column])
                    {
                        tbl.Columns.Add(hasHeader ? firstRowCell.Text : string.Format("Column {0}", firstRowCell.Start.Column));
                    }
                    var startRow = hasHeader ? 2 : 1;
                    for (int rowNum = startRow; rowNum <= ws.Dimension.End.Row; rowNum++)
                    {
                        var wsRow = ws.Cells[rowNum, 1, rowNum, ws.Dimension.End.Column];
                        DataRow row = tbl.Rows.Add();

                        foreach (var cell in wsRow)
                        {
                            row[cell.Start.Column - 1] = cell.Text.Trim() == "" ? "0.00" : cell.Text.Trim() == "-" ? "0.00" : cell.Text.Trim();
                        }
                    }
                    rows = hasHeader ? ws.Dimension.End.Row - 1 : ws.Dimension.End.Row;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetDataTableFromExcel error: {ex}");
                helperLog.LogWrite($"GetDataTableFromExcel error: {ex}");
                tbl = null;
            }
            return tbl;
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
