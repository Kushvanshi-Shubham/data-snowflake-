using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
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
using DataTable = System.Data.DataTable;
using Excel = Microsoft.Office.Interop.Excel;
using Path = System.IO.Path;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Excel_ST_MAJ_CAT_FIXT_PLAN_Console
{
    internal class Program
    {
       
        static string filename = "", startTime = "", endTime = "", comments = "";
        static int rows=0, rowsfetched=0;
        static void Main(string[] args)
        {
        
            LogWriter log = new LogWriter("Import excel");
            try
            {
                log.LogWrite("nikhil".ToString());
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                //string strFileName = @"D:\01-MIS\01-MASTER\ST-MASTER.xlsx";
                string strFileName = @"\\Mis\01-mis\01-MASTER\ST-MASTER.xlsx";
                // string strFileName = @"\\192.168.151.27\d\01-MIS\01-MASTER\BROADER MENU.xlsx";
                System.Data.DataTable tbl = new System.Data.DataTable();

              
                using (ExcelPackage package = new ExcelPackage(new FileInfo(strFileName)))
                {
                    int i = 0;
                    foreach (var worksheet1 in package.Workbook.Worksheets)
                    {
                       
                        
                            log.LogWrite("check".ToString());
                            filename = worksheet1.ToString();
                            startTime = DateTime.Now.ToString();


                            tbl = LinqHelper.GetDataTableFromExcel(strFileName, ref rows, true, i);
                            rowsfetched = tbl.Rows.Count;
                            comments= rows == rowsfetched ? "Successful" : "Data Missed";

                        tbl.TableName = "STORE_PLANT_MASTER";
                            string tableDDL = "";
                           
                            tableDDL += "truncate table [dbo].[" + tbl.TableName + "] ";
                           

                          

                            string consString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
                            using (SqlConnection con = new SqlConnection(consString))
                            {

                                using (SqlCommand SQLCmd = new SqlCommand(tableDDL, con))
                                {

                                    con.Open();
                                  
                                    SQLCmd.ExecuteNonQuery();
                                    con.Close();
                                    log.LogWrite("Drop/Delete SQL Run");
                                }
                            using (SqlBulkCopy sqlBulkCopy = new SqlBulkCopy(con))
                            {
                                sqlBulkCopy.DestinationTableName = tbl.TableName;
                                con.Open();
                                sqlBulkCopy.BatchSize = 10000;
                                sqlBulkCopy.BulkCopyTimeout = 0;
                                sqlBulkCopy.WriteToServer(tbl);
                                con.Close();
                                endTime = DateTime.Now.ToString();
                                log.LogWrite("Data inserted Successfully ");
                            }


                        }


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
        public static System.Data.DataTable GetDataTableFromExcel(string path,ref int rows, bool hasHeader = true, int WorksheetNumber = 0)
        {
            using (var pck = new OfficeOpenXml.ExcelPackage())
            {
                using (var stream = File.OpenRead(path))
                {
                    pck.Load(stream);
                }
                var ws = pck.Workbook.Worksheets[WorksheetNumber];//.First();
                System.Data.DataTable tbl = new System.Data.DataTable(ws.Name);
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
                return tbl;
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

    //public static class sendMail
    //{
    //    public static void send(string subject,string message)
    //    {
    //        var email = new MimeMessage();



    //        email.From.Add(new MailboxAddress("Sender", "nikhil.chhokra@v2kart.com"));
    //        email.To.Add(new MailboxAddress("Receiver Name", "nikhil.chhokra@v2kart.com"));

    //        email.Subject = subject;
    //        email.Body = new TextPart("html") { Text = message };
    //        using (var smtp = new SmtpClient())
    //        {
    //            smtp.Connect("smtp.gmail.com", 587, false);

    //            // Note: only needed if the SMTP server requires authentication

    //            smtp.Authenticate("nikhil.chhokra@v2kart.com", "V2kart@nikhil");
    //            //smtp.Send(email);
    //            //smtp.Disconnect(true);
    //        }
    //    }
    //}
}