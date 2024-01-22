using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;

public class PayrollUser
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string Id { get; set; }

    public string Name { get; set; }

    public string Email { get; set; }
    public string Password { get; set; }
    public ICollection<PayrollInvoice> PayrollInvoices { get; set; } = new List<PayrollInvoice>();

    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}
