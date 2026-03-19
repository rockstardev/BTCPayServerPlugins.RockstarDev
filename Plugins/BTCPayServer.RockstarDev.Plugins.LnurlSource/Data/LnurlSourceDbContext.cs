#nullable enable
using BTCPayServer.RockstarDev.Plugins.LnurlSource.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.LnurlSource.Data;

public class LnurlSourceDbContext(DbContextOptions<LnurlSourceDbContext> options)
    : DbContext(options)
{
    public DbSet<LnurlSourceInvoice> Invoices { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("BTCPayServer.RockstarDev.Plugins.LnurlSource");

        modelBuilder.Entity<LnurlSourceInvoice>()
            .HasIndex(e => e.InvoiceId);
    }
}
