using System.Collections.Generic;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.ViewModels.PayrollUser
{
    public class PayrollUserListViewModel
    {
        public List<Data.Models.PayrollUser> PayrollUsers { get; set; }
        public bool All { get; set; }
    }
}
