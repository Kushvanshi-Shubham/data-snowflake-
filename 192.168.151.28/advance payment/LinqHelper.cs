using System;
using System.Data;
using SAP.Middleware.Connector;

namespace RFC_ZADVANCE_PAYMENT
{
    /// <summary>
    /// Converts an SAP IRfcTable to an ADO.NET DataTable.
    /// Handles all common SAP data types: CHAR, STRING, DATE, BCD, INT2, INT4, FLOAT.
    /// Appends a SQL_DATE column stamped with the run datetime.
    /// </summary>
    public static class LinqHelper
    {
        public static DataTable ToSqlDataTable(this IRfcTable sapTable, string tableName, DateTime runDate)
        {
            var dt = new DataTable(tableName);

            // ── Schema: one column per RFC field ────────────────────────────
            for (int i = 0; i < sapTable.ElementCount; i++)
            {
                RfcElementMetadata meta = sapTable.GetElementMetadata(i);
                dt.Columns.Add(meta.Name, MapToClrType(meta.DataType));
            }

            // Audit column — which run produced this row
            dt.Columns.Add("SQL_DATE", typeof(DateTime)).DefaultValue = runDate.Date;

            // ── Rows ─────────────────────────────────────────────────────────
            foreach (IRfcStructure row in sapTable)
            {
                DataRow dr = dt.NewRow();

                for (int i = 0; i < sapTable.ElementCount; i++)
                {
                    RfcElementMetadata meta = sapTable.GetElementMetadata(i);

                    try
                    {
                        switch (meta.DataType)
                        {
                            case RfcDataType.DATE:
                                // SAP DATE comes as "YYYY-MM-DD" — store as "YYYYMMDD"
                                string raw = row.GetString(meta.Name);
                                dr[meta.Name] = raw.Length >= 10
                                    ? raw.Substring(0, 4) + raw.Substring(5, 2) + raw.Substring(8, 2)
                                    : raw;
                                break;

                            case RfcDataType.BCD:
                                dr[meta.Name] = row.GetDecimal(meta.Name);
                                break;

                            case RfcDataType.INT2:
                            case RfcDataType.INT4:
                                dr[meta.Name] = row.GetInt(meta.Name);
                                break;

                            case RfcDataType.FLOAT:
                                dr[meta.Name] = row.GetDouble(meta.Name);
                                break;

                            case RfcDataType.CHAR:
                            case RfcDataType.STRING:
                            default:
                                dr[meta.Name] = row.GetString(meta.Name);
                                break;
                        }
                    }
                    catch
                    {
                        dr[meta.Name] = DBNull.Value;
                    }
                }

                dt.Rows.Add(dr);
            }

            return dt;
        }

        static Type MapToClrType(RfcDataType rfcType)
        {
            switch (rfcType)
            {
                case RfcDataType.BCD:    return typeof(decimal);
                case RfcDataType.INT2:
                case RfcDataType.INT4:   return typeof(int);
                case RfcDataType.FLOAT:  return typeof(double);
                default:                 return typeof(string);
            }
        }
    }
}
