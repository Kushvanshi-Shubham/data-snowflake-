using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RFC_Seasonal_Stock_Application
{
    public class LogEntry
    {
        public DateTime SAP_START_TIME { get; set; }
        public DateTime SAP_END_TIME { get; set; }
        public DateTime SQL_START { get; set; }
        public DateTime SQL_END { get; set; }
        public int SAP_ROW_FETCH_COUNT { get; set; }
        public int SAP_ROW_PUSH_COUNT { get; set; }
        public string Message { get; set; }
        public bool Status { get; set; }

        public LogEntry()
        {

        }
        public LogEntry(DateTime sAP_START_TIME, DateTime sAP_END_TIME, DateTime sQL_START, DateTime sQL_END, int sAP_ROW_FETCH_COUNT, int sAP_ROW_PUSH_COUNT, string Message, bool Status)
        {


            SAP_START_TIME = sAP_START_TIME;
            SAP_END_TIME = sAP_END_TIME;
            SQL_START = sQL_START;
            SQL_END = sQL_END;
            SAP_ROW_FETCH_COUNT = sAP_ROW_FETCH_COUNT;
            SAP_ROW_PUSH_COUNT = sAP_ROW_PUSH_COUNT;
            Message = Message;
            Status = Status;
        }


    }
}
