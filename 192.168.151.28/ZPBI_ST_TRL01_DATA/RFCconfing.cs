using SAP.Middleware.Connector;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZPBI_ST_TRL01_DATA
{
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
