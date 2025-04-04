using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;

public class PayrollSetting
{
    [Key]
    [MaxLength(50)]
    public string StoreId { get; set; }

    public string Setting { get; set; }
}
