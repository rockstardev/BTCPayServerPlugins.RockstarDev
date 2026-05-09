using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.RockstarDev.Plugins.OfflinePayments.Data;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.RockstarDev.Plugins.OfflinePayments.Services;

public class OfflinePaymentsService(OfflinePaymentPluginDbContextFactory pluginDbContextFactory, InvoiceRepository invoiceRepository)
{
    public async Task<OfflinePendingPayment> RecordMethodSelected(string storeId, string invoiceId, OfflineMethodConfig config)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var reference = config.ReferenceTemplate.Replace("{InvoiceId}", invoiceId).Replace("{StoreId}", storeId);
        var pending = new OfflinePendingPayment
        {
            StoreId = storeId,
            InvoiceId = invoiceId,
            MethodConfigId = config.Id,
            MethodId = config.MethodId,
            ResolvedReference = reference,
            Status = OfflinePaymentStatus.MethodSelected
        };
        ctx.OfflinePendingPayments.Add(pending);
        await ctx.SaveChangesAsync();
        return pending;
    }

    public async Task MarkInstructionsAsViewed(string pendingPaymentId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var pendingPayment = await ctx.OfflinePendingPayments.FindAsync(pendingPaymentId);
        if (pendingPayment is null)
            return;

        if (pendingPayment.Status == OfflinePaymentStatus.MethodSelected)
            pendingPayment.Status = OfflinePaymentStatus.InstructionsViewed;

        pendingPayment.InstructionsViewedAt ??= DateTimeOffset.UtcNow;
        pendingPayment.UpdatedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync();
    }

    public async Task<OfflinePendingPayment> CustomerMarkSent(string storeId, string invoiceId, string customerNote = null, string remittanceUrl = null)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var pendingPayment = await ctx.OfflinePendingPayments.Include(x => x.MethodConfig).Where(x => x.StoreId == storeId && x.InvoiceId == invoiceId)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();

        if (pendingPayment is null)
            return null;

        pendingPayment.Status = OfflinePaymentStatus.CustomerMarkedSent;
        pendingPayment.CustomerMarkedSentAt = DateTimeOffset.UtcNow;
        pendingPayment.CustomerNote = customerNote;
        pendingPayment.RemittanceFileUrl = remittanceUrl;
        pendingPayment.UpdatedAt = DateTimeOffset.UtcNow;
        await ctx.SaveChangesAsync();
        return pendingPayment;
    }

    public async Task<List<OfflinePendingPayment>> GetPendingPaymentQueue(string storeId, string methodIdFilter = null)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var query = ctx.OfflinePendingPayments.Include(x => x.MethodConfig).Where(x => x.StoreId == storeId
            && (x.Status == OfflinePaymentStatus.CustomerMarkedSent || x.Status == OfflinePaymentStatus.InstructionsViewed || x.Status == OfflinePaymentStatus.MethodSelected));

        if (!string.IsNullOrEmpty(methodIdFilter))
            query = query.Where(x => x.MethodId == methodIdFilter);

        return await query.OrderBy(x => x.CreatedAt).ToListAsync();
    }

    public async Task<OfflinePendingPayment> AdminConfirmPayment(string pendingPaymentId, string storeId, string adminUserId, string adminNote = null)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var pendingPayment = await ctx.OfflinePendingPayments.FirstOrDefaultAsync(x => x.Id == pendingPaymentId && x.StoreId == storeId);
        if (pendingPayment is null)
            return null;

        pendingPayment.Status = OfflinePaymentStatus.AdminConfirmed;
        pendingPayment.AdminConfirmedAt = DateTimeOffset.UtcNow;
        pendingPayment.AdminUserId = adminUserId;
        pendingPayment.AdminNote = adminNote;
        pendingPayment.UpdatedAt = DateTimeOffset.UtcNow;
        var invoice = await invoiceRepository.GetInvoice(pendingPayment.InvoiceId);
        if (invoice != null)
        {
            await invoiceRepository.MarkInvoiceStatus(pendingPayment.InvoiceId, InvoiceStatus.Settled);
        }
        await ctx.SaveChangesAsync();
        return pendingPayment;
    }

    public async Task<OfflinePendingPayment> AdminInvalidatePayment(string pendingPaymentId, string storeId, string adminUserId, string adminNote = null)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var pendingPayment = await ctx.OfflinePendingPayments.FirstOrDefaultAsync(x => x.Id == pendingPaymentId && x.StoreId == storeId);
        if (pendingPayment is null)
            return null;

        pendingPayment.Status = OfflinePaymentStatus.AdminInvalidated;
        pendingPayment.AdminInvalidatedAt = DateTimeOffset.UtcNow;
        pendingPayment.AdminUserId = adminUserId;
        pendingPayment.AdminNote = adminNote;
        pendingPayment.UpdatedAt = DateTimeOffset.UtcNow;
        var invoice = await invoiceRepository.GetInvoice(pendingPayment.InvoiceId);
        if (invoice != null)
        {
            await invoiceRepository.MarkInvoiceStatus(pendingPayment.InvoiceId, InvoiceStatus.Settled);
        }
        await ctx.SaveChangesAsync();
        return pendingPayment;
    }

    public async Task<OfflinePendingPayment> GetByInvoiceId(string storeId, string invoiceId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        return await ctx.OfflinePendingPayments.Include(x => x.MethodConfig).Where(x => x.StoreId == storeId && x.InvoiceId == invoiceId)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
    }
}
