using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Data.Models;

public class VendorPaySetting
{
    [Key]
    [MaxLength(50)]
    public string StoreId { get; set; }

    public string Setting { get; set; }
}