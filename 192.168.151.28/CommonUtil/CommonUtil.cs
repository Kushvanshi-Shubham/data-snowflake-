using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.Data.SqlClient;
using System.Data;

namespace CommonUtil
{
    public class CommonUtil
    {
        public string RFC_NAME { get; set; }
        public DateTime DATA_PULLFOR_DATE { get; set; }
        public string STORE { get; set; }
        public DateTime RFC_START_TIME { get; set; }
        public DateTime RFC_END_TIME { get; set; }
        public Int64 RFC_PULL_Records { get; set; }
        public Int64 RFC_PUSH_Records { get; set; }
        public string RFC_MESSAGE { get; set; }
        public string SAP_TABLE { get; set; }
        public DateTime SQL_START_TIME { get; set; }
        public DateTime SQL_END_TIME { get; set; }
        public Int64 SQL_PULL_Records { get; set; }
        public Int64 SQL_PUSH_Records { get; set; }
        public string SQL_MESSAGE { get; set; }
        public bool IsSuccess { get; set; }

        string conString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ToString();
        public int logDetails()
        {
            int iResult = 0;
            SqlCommand SqlCom = null;
            SqlConnection SqlCon = null;
            try
            {
                SqlCon = new SqlConnection(conString);
                SqlCom = new SqlCommand("USP_Dashboard_TRACKERLog", SqlCon);
                SqlCom.CommandType = CommandType.StoredProcedure;
                SqlCom.Parameters.AddWithValue("@RFC_NAME", RFC_NAME);
                SqlCom.Parameters.AddWithValue("@DATA_PULLFOR_DATE", DATA_PULLFOR_DATE);
                SqlCom.Parameters.AddWithValue("@STORE", STORE);
                SqlCom.Parameters.AddWithValue("@RFC_START_TIME", RFC_START_TIME);
                SqlCom.Parameters.AddWithValue("@RFC_END_TIME", RFC_END_TIME);
                SqlCom.Parameters.AddWithValue("@RFC_PULL_Records", RFC_PULL_Records);
                SqlCom.Parameters.AddWithValue("@RFC_PUSH_Records", RFC_PUSH_Records);
                SqlCom.Parameters.AddWithValue("@RFC_MESSAGE", RFC_MESSAGE);
                SqlCom.Parameters.AddWithValue("@SAP_TABLE", SAP_TABLE);
                SqlCom.Parameters.AddWithValue("@SQL_START_TIME", SQL_START_TIME);
                SqlCom.Parameters.AddWithValue("@SQL_END_TIME", SQL_END_TIME);
                SqlCom.Parameters.AddWithValue("@SQL_PULL_Records", SQL_PULL_Records);
                SqlCom.Parameters.AddWithValue("@SQL_PUSH_Records", SQL_PUSH_Records);
                SqlCom.Parameters.AddWithValue("@SQL_MESSAGE", SQL_MESSAGE);
                SqlCom.Parameters.AddWithValue("@IsSuccess", IsSuccess);
                SqlCon.Open();
                iResult = SqlCom.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                iResult = -9;
            }
            finally
            {
                if (SqlCon != null && SqlCon.State != ConnectionState.Closed)
                {
                    SqlCon.Close();
                }
                if (SqlCom != null)
                {
                    SqlCom.Dispose();
                }
            }
            return iResult;
        }

        public int logMaster()
        {
            int iResult = 0;
            SqlCommand SqlCom = null;
            SqlConnection SqlCon = null;
            try
            {
                SqlCon = new SqlConnection(conString);
                SqlCom = new SqlCommand("USP_Dashboard_TRACKERlog_MASTER", SqlCon);
                SqlCom.CommandType = CommandType.StoredProcedure;
                SqlCom.Parameters.AddWithValue("@RFC_NAME", RFC_NAME);
                SqlCom.Parameters.AddWithValue("@RFC_START_TIME", RFC_START_TIME);
                SqlCom.Parameters.AddWithValue("@RFC_END_TIME", RFC_END_TIME);
                SqlCom.Parameters.AddWithValue("@RFC_MESSAGE", RFC_MESSAGE);
                SqlCom.Parameters.AddWithValue("@SAP_TABLE", SAP_TABLE);
                SqlCom.Parameters.AddWithValue("@IsSuccess", IsSuccess);
                SqlCon.Open();
                iResult = SqlCom.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                iResult = -9;
            }
            finally
            {
                if (SqlCon != null && SqlCon.State != ConnectionState.Closed)
                {
                    SqlCon.Close();
                }
                if (SqlCom != null)
                {
                    SqlCom.Dispose();
                }
            }
            return iResult;
        }
    }
}
