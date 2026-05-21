using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using SAP.Middleware.Connector;

namespace RFC_ZADVANCE_PAYMENT
{
    internal class Program
    {
        // ── Tracking fields ──────────────────────────────────────────────────
        static string  RFC_NAME          = "ZADVANCE_PAYMENT_RFC";
        static string  SAP_TABLE         = "ET_ZADVANCE_PAYMENT";
        static string  RFC_MESSAGE       = "";
        static string  SQL_MESSAGE       = "";
        static bool    IsSuccess         = true;

        static DateTime G_RFC_START_TIME;
        static DateTime RFC_START_TIME, RFC_END_TIME;
        static DateTime SQL_START_TIME, SQL_END_TIME;
        static DateTime DATA_PULLFOR_DATE;

        static long RFC_PULL_Records, RFC_PUSH_Records;
        static long SQL_PULL_Records, SQL_PUSH_Records;

        // ── Entry point ──────────────────────────────────────────────────────
        static void Main(string[] args)
        {
            G_RFC_START_TIME = DateTime.Now;
            DATA_PULLFOR_DATE = DateTime.Now.AddDays(-1);

            string connString = ConfigurationManager
                .ConnectionStrings["DefaultConnection"].ConnectionString;

            // ── Date window: driven by App.config or defaults ────────────────
            // Override via args: RFC_ZADVANCE_PAYMENT.exe 20250101 20250331
            string dateFrom = args.Length >= 1
                ? args[0]
                : DateTime.Now.AddMonths(-1).ToString("yyyyMMdd");

            string dateTo = args.Length >= 2
                ? args[1]
                : DateTime.Now.AddDays(-1).ToString("yyyyMMdd");

            string companyCode = args.Length >= 3
                ? args[2]
                : ConfigurationManager.AppSettings["COMPANY_CODE"] ?? "1000";

            Console.WriteLine($"[{RFC_NAME}] Starting pull");
            Console.WriteLine($"  Company Code : {companyCode}");
            Console.WriteLine($"  Date From    : {dateFrom}");
            Console.WriteLine($"  Date To      : {dateTo}");
            Console.WriteLine($"  Target Table : {SAP_TABLE}");
            Console.WriteLine();

            RFC_START_TIME = DateTime.Now;
            RFC_MESSAGE    = "Success";
            IsSuccess      = true;

            try
            {
                // ── 1. Connect to SAP ────────────────────────────────────────
                RfcConfigParameters rfcPar = RFCConfig.GetParameters();
                RfcDestination      dest   = RfcDestinationManager.GetDestination(rfcPar);
                RfcRepository       repo   = dest.Repository;

                // ── 2. Call RFC ──────────────────────────────────────────────
                IRfcFunction myfun = repo.CreateFunction("ZADVANCE_PAYMENT_RFC");

                myfun.SetValue("I_COMPANY_CODE",      companyCode);
                //myfun.SetValue("I_POSTING_DATE_LOW",  dateFrom);
                //myfun.SetValue("I_POSTING_DATE_HIGH", dateTo);

                Console.WriteLine("  Invoking RFC...");
                myfun.Invoke(dest);

                // ── 3. Check EX_RETURN ───────────────────────────────────────
                IRfcStructure exReturn   = myfun.GetStructure("EX_RETURN");
                string        returnType = exReturn.GetValue("TYPE").ToString();
                string        returnMsg  = exReturn.GetValue("MESSAGE").ToString();

                if (returnType == "E")
                {
                    throw new Exception($"SAP returned error: {returnMsg}");
                }

                // ── 4. Read IT_FINAL table ───────────────────────────────────
                IRfcTable rfcTable = myfun.GetTable("IT_FINAL");
                int rowCount = rfcTable.RowCount;
                Console.WriteLine($"  RFC returned {rowCount:N0} rows.");

                RFC_END_TIME     = DateTime.Now;
                RFC_PULL_Records = rowCount;
                RFC_PUSH_Records = rowCount;

                // ── 5. Convert to DataTable ──────────────────────────────────
                DataTable dt = rfcTable.ToSqlDataTable(SAP_TABLE, DateTime.Now);
                Console.WriteLine($"  DataTable columns : {dt.Columns.Count}");

                // ── 6. Create / Truncate SQL table, then BulkCopy ────────────
                SQL_START_TIME   = DateTime.Now;
                SQL_PULL_Records = dt.Rows.Count;

                string ddl = BuildDDL(dt);

                using (SqlConnection con = new SqlConnection(connString))
                {
                    con.Open();

                    // Create if not exists, truncate if exists
                    using (SqlCommand cmd = new SqlCommand(ddl, con))
                    {
                        cmd.CommandTimeout = 300;
                        cmd.ExecuteNonQuery();
                        Console.WriteLine("  Table ready (created/truncated).");
                    }

                    // Bulk insert
                    using (SqlBulkCopy bcp = new SqlBulkCopy(con))
                    {
                        bcp.DestinationTableName = SAP_TABLE;
                        bcp.BatchSize            = 100_000;
                        bcp.BulkCopyTimeout      = 0;
                        bcp.WriteToServer(dt);
                    }

                    con.Close();
                }

                SQL_END_TIME     = DateTime.Now;
                SQL_MESSAGE      = "Success";
                SQL_PUSH_Records = SQL_PULL_Records;

                Console.WriteLine($"  Pushed {SQL_PUSH_Records:N0} rows to [{SAP_TABLE}].");
            }
            catch (Exception ex)
            {
                IsSuccess      = false;
                RFC_MESSAGE    = ex.Message;
                SQL_MESSAGE    = ex.Message;
                SQL_END_TIME   = DateTime.Now;
                SQL_PUSH_Records = 0;
                Console.WriteLine($"  ERROR: {ex.Message}");
            }

            // ── 7. Log ───────────────────────────────────────────────────────
            WriteLog(connString);

            Console.WriteLine();
            Console.WriteLine($"  Status   : {(IsSuccess ? "SUCCESS" : "FAILED")}");
            Console.WriteLine($"  RFC Time : {(RFC_END_TIME - RFC_START_TIME).TotalSeconds:F1}s");
            Console.WriteLine($"  SQL Time : {(SQL_END_TIME - SQL_START_TIME).TotalSeconds:F1}s");
            Console.WriteLine($"  Total    : {(DateTime.Now - G_RFC_START_TIME).TotalSeconds:F1}s");
        }

        // ── DDL: Create if not exists + Truncate if exists ───────────────────
        static string BuildDDL(DataTable dt)
        {
            var sql = new System.Text.StringBuilder();

            // Create block
            sql.Append($"IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[{dt.TableName}]') AND type = N'U') BEGIN ");
            sql.Append(GetCreateTableSql(dt));
            sql.Append(" END ");

            // Truncate block
            sql.Append($"IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[{dt.TableName}]') AND type = N'U') BEGIN ");
            sql.Append($" TRUNCATE TABLE [dbo].[{dt.TableName}] ");
            sql.Append("END");

            return sql.ToString();
        }

        static string GetCreateTableSql(DataTable table)
        {
            var sql = new System.Text.StringBuilder();
            sql.AppendFormat("CREATE TABLE [dbo].[{0}] (", table.TableName);

            foreach (DataColumn col in table.Columns)
            {
                sql.AppendFormat("\n\t[{0}]", col.ColumnName);

                switch (col.DataType.ToString().ToUpper())
                {
                    case "SYSTEM.INT16":   sql.Append(" smallint");        break;
                    case "SYSTEM.INT32":   sql.Append(" int");             break;
                    case "SYSTEM.INT64":   sql.Append(" bigint");          break;
                    case "SYSTEM.DATETIME":sql.Append(" datetime");        break;
                    case "SYSTEM.DECIMAL": sql.Append(" decimal(18,6)");   break;
                    case "SYSTEM.DOUBLE":  sql.Append(" float");           break;
                    case "SYSTEM.SINGLE":  sql.Append(" real");            break;
                    default:               sql.Append(" nvarchar(max)");   break;
                }
                sql.Append(",");
            }

            // Remove trailing comma
            sql.Remove(sql.Length - 1, 1);
            sql.Append("\n);");
            return sql.ToString();
        }

        // ── Log to SQL tracking table ─────────────────────────────────────────
        static void WriteLog(string connString)
        {
            try
            {
                string logDDL = @"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[RFC_PIPELINE_LOG]') AND type = N'U')
                    CREATE TABLE [dbo].[RFC_PIPELINE_LOG] (
                        [ID]               bigint IDENTITY(1,1) PRIMARY KEY,
                        [RFC_NAME]         nvarchar(100),
                        [SAP_TABLE]        nvarchar(100),
                        [DATA_PULLFOR_DATE]datetime,
                        [RFC_START_TIME]   datetime,
                        [RFC_END_TIME]     datetime,
                        [RFC_PULL_Records] bigint,
                        [RFC_PUSH_Records] bigint,
                        [RFC_MESSAGE]      nvarchar(max),
                        [SQL_START_TIME]   datetime,
                        [SQL_END_TIME]     datetime,
                        [SQL_PULL_Records] bigint,
                        [SQL_PUSH_Records] bigint,
                        [SQL_MESSAGE]      nvarchar(max),
                        [IsSuccess]        bit,
                        [LOGGED_AT]        datetime DEFAULT GETDATE()
                    )";

                string insertLog = @"
                    INSERT INTO [dbo].[RFC_PIPELINE_LOG]
                    (RFC_NAME, SAP_TABLE, DATA_PULLFOR_DATE,
                     RFC_START_TIME, RFC_END_TIME, RFC_PULL_Records, RFC_PUSH_Records, RFC_MESSAGE,
                     SQL_START_TIME, SQL_END_TIME, SQL_PULL_Records, SQL_PUSH_Records, SQL_MESSAGE, IsSuccess)
                    VALUES
                    (@RFC_NAME, @SAP_TABLE, @DATA_PULLFOR_DATE,
                     @RFC_START_TIME, @RFC_END_TIME, @RFC_PULL_Records, @RFC_PUSH_Records, @RFC_MESSAGE,
                     @SQL_START_TIME, @SQL_END_TIME, @SQL_PULL_Records, @SQL_PUSH_Records, @SQL_MESSAGE, @IsSuccess)";

                using (SqlConnection con = new SqlConnection(connString))
                {
                    con.Open();

                    new SqlCommand(logDDL, con) { CommandTimeout = 60 }.ExecuteNonQuery();

                    using (SqlCommand cmd = new SqlCommand(insertLog, con))
                    {
                        cmd.Parameters.AddWithValue("@RFC_NAME",          RFC_NAME);
                        cmd.Parameters.AddWithValue("@SAP_TABLE",         SAP_TABLE);
                        cmd.Parameters.AddWithValue("@DATA_PULLFOR_DATE", DATA_PULLFOR_DATE);
                        cmd.Parameters.AddWithValue("@RFC_START_TIME",    RFC_START_TIME == default ? (object)DBNull.Value : RFC_START_TIME);
                        cmd.Parameters.AddWithValue("@RFC_END_TIME",      RFC_END_TIME   == default ? (object)DBNull.Value : RFC_END_TIME);
                        cmd.Parameters.AddWithValue("@RFC_PULL_Records",  RFC_PULL_Records);
                        cmd.Parameters.AddWithValue("@RFC_PUSH_Records",  RFC_PUSH_Records);
                        cmd.Parameters.AddWithValue("@RFC_MESSAGE",       RFC_MESSAGE ?? "");
                        cmd.Parameters.AddWithValue("@SQL_START_TIME",    SQL_START_TIME == default ? (object)DBNull.Value : SQL_START_TIME);
                        cmd.Parameters.AddWithValue("@SQL_END_TIME",      SQL_END_TIME   == default ? (object)DBNull.Value : SQL_END_TIME);
                        cmd.Parameters.AddWithValue("@SQL_PULL_Records",  SQL_PULL_Records);
                        cmd.Parameters.AddWithValue("@SQL_PUSH_Records",  SQL_PUSH_Records);
                        cmd.Parameters.AddWithValue("@SQL_MESSAGE",       SQL_MESSAGE ?? "");
                        cmd.Parameters.AddWithValue("@IsSuccess",         IsSuccess);
                        cmd.ExecuteNonQuery();
                    }

                    con.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [LOG ERROR] {ex.Message}");
            }
        }
    }
}
