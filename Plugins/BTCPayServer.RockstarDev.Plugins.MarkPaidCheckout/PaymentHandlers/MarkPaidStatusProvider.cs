using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout.PaymentHandlers;

public class MarkPaidStatusProvider(StoreRepository storeRepository, PaymentMethodHandlerDictionary handlers)
{
    public async Task<bool> IsEnabled(string storeId, PaymentMethodId pmid)
    {
        try
        {
            var storeData = await storeRepository.FindStore(storeId);
            if (storeData == null)
                return false;
            var currentPaymentMethodConfig = storeData.GetPaymentMethodConfig<MarkPaidPaymentMethodConfig>(pmid, handlers);
            if (currentPaymentMethodConfig == null)
                return false;
            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();
            return !excludeFilters.Match(pmid);
        }
        catch { return false; }
    }
}
