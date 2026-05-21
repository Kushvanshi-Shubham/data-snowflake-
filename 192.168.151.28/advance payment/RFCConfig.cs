using System.Configuration;
using SAP.Middleware.Connector;

namespace RFC_ZADVANCE_PAYMENT
{
    public static class RFCConfig
    {
        public static RfcConfigParameters GetParameters()
        {
            var rfcPar = new RfcConfigParameters();

            rfcPar.Add(RfcConfigParameters.Name,          ConfigurationManager.AppSettings["SAP_Name"]);
            rfcPar.Add(RfcConfigParameters.AppServerHost, ConfigurationManager.AppSettings["SAP_AppServerHost"]);
            rfcPar.Add(RfcConfigParameters.Client,        ConfigurationManager.AppSettings["SAP_Client"]);
            rfcPar.Add(RfcConfigParameters.User,          ConfigurationManager.AppSettings["SAP_User"]);
            rfcPar.Add(RfcConfigParameters.Password,      ConfigurationManager.AppSettings["SAP_Password"]);
            rfcPar.Add(RfcConfigParameters.SystemNumber,  ConfigurationManager.AppSettings["SAP_SystemNumber"]);
            rfcPar.Add(RfcConfigParameters.Language,      ConfigurationManager.AppSettings["SAP_Language"] ?? "EN");

            return rfcPar;
        }
    }
}
