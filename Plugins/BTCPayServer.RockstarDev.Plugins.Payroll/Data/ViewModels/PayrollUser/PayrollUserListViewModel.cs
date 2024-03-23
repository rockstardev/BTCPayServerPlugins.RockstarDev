using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using System.Collections.Generic;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data
{
    public class PayrollUserListViewModel
    {
        public List<PayrollUser> PayrollUsers { get; set; }
        public PayrollUserActiveState ActiveState { get; set; }
    }
}
