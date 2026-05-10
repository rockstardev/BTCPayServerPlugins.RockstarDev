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
    public async Task<OfflinePendingPayment> RecordPayment(string storeId, string invoiceId, OfflineMethodConfig config, string customerNote = null, string remittanceUrl = null)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var reference = config.ReferenceTemplate.Replace("{InvoiceId}", invoiceId).Replace("{StoreId}", storeId);
        var payment = new OfflinePendingPayment
        {
            StoreId = storeId,
            InvoiceId = invoiceId,
            MethodConfigId = config.Id,
            MethodId = config.MethodId,
            CustomerNote = customerNote,
            RemittanceFileUrl = remittanceUrl,
            ResolvedReference = reference,
            CustomerMarkedSentAt = DateTimeOffset.UtcNow,
            Status = OfflinePaymentStatus.CustomerMarkedSent
        };
        ctx.OfflinePendingPayments.Add(payment);
        await ctx.SaveChangesAsync();
        return payment;
    }

    public async Task<List<OfflinePendingPayment>> GetPendingPaymentQueue(string storeId, string methodIdFilter = null)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var query = ctx.OfflinePendingPayments.Include(x => x.MethodConfig).Where(x => x.StoreId == storeId && x.Status == OfflinePaymentStatus.CustomerMarkedSent);
        if (!string.IsNullOrEmpty(methodIdFilter))
            query = query.Where(x => x.MethodId == methodIdFilter);

        return await query.OrderBy(x => x.CustomerMarkedSentAt).ToListAsync();
    }

    public async Task<OfflinePendingPayment> AdminConfirmPayment(string pendingPaymentId, string storeId, string adminUserId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var pendingPayment = await ctx.OfflinePendingPayments.FirstOrDefaultAsync(x => x.Id == pendingPaymentId && x.StoreId == storeId);
        if (pendingPayment is null)
            return null;

        pendingPayment.Status = OfflinePaymentStatus.AdminConfirmed;
        pendingPayment.AdminConfirmedAt = DateTimeOffset.UtcNow;
        pendingPayment.AdminUserId = adminUserId;
        pendingPayment.UpdatedAt = DateTimeOffset.UtcNow;
        var invoice = await invoiceRepository.GetInvoice(pendingPayment.InvoiceId);
        if (invoice != null)
        {
            await invoiceRepository.MarkInvoiceStatus(pendingPayment.InvoiceId, InvoiceStatus.Settled);
        }
        await ctx.SaveChangesAsync();
        return pendingPayment;
    }

    public async Task<OfflinePendingPayment> AdminInvalidatePayment(string pendingPaymentId, string storeId, string adminUserId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        var pendingPayment = await ctx.OfflinePendingPayments.FirstOrDefaultAsync(x => x.Id == pendingPaymentId && x.StoreId == storeId);
        if (pendingPayment is null)
            return null;

        pendingPayment.Status = OfflinePaymentStatus.AdminInvalidated;
        pendingPayment.AdminInvalidatedAt = DateTimeOffset.UtcNow;
        pendingPayment.AdminUserId = adminUserId;
        pendingPayment.UpdatedAt = DateTimeOffset.UtcNow;
        var invoice = await invoiceRepository.GetInvoice(pendingPayment.InvoiceId);
        if (invoice != null)
        {
            await invoiceRepository.MarkInvoiceStatus(pendingPayment.InvoiceId, InvoiceStatus.Invalid);
        }
        await ctx.SaveChangesAsync();
        return pendingPayment;
    }

    public async Task<OfflinePendingPayment> GetByInvoiceId(string storeId, string invoiceId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        return await ctx.OfflinePendingPayments.FirstOrDefaultAsync(x => x.StoreId == storeId && x.InvoiceId == invoiceId);
    }

    public async Task<bool> HasPendingPayments(string storeId)
    {
        await using var ctx = pluginDbContextFactory.CreateContext();
        return await ctx.OfflinePendingPayments.AnyAsync(x => x.StoreId == storeId && x.Status == OfflinePaymentStatus.CustomerMarkedSent);
    }
}
