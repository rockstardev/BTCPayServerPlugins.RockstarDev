using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.RockstarDev.Plugins.Payroll
{
    // Need to extract these variables, their references are places for improvements
    public class PayrollPluginConst
    {
        public const string CURRENCY_FIAT = "USD";

        // TODO: Is there a better way here to make it more generic?
        public const string BTC_CRYPTOCODE = "BTC";

        // TODO: Extrac this outside
        public const string ADMIN_USER_ID = "79023d5c-7fdf-4277-923d-ca8e37ab2d31";
    }
}
