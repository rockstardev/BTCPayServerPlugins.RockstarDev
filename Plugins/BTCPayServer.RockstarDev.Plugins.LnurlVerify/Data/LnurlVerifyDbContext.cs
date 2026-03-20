#nullable enable
using BTCPayServer.RockstarDev.Plugins.LnurlVerify.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.LnurlVerify.Data;

public class LnurlVerifyDbContext(DbContextOptions<LnurlVerifyDbContext> options)
    : DbContext(options)
{
    public DbSet<LnurlVerifyInvoice> Invoices { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("BTCPayServer.RockstarDev.Plugins.LnurlVerify");

        modelBuilder.Entity<LnurlVerifyInvoice>()
            .HasIndex(e => e.InvoiceId);
    }
}
