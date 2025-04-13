using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckout.PaymentHandlers;

public class CashStatusProvider(
    StoreRepository storeRepository,
    PaymentMethodHandlerDictionary handlers)
{
    public async Task<bool> CashEnabled(string storeId)
    {
        try
        {
            var storeData = await storeRepository.FindStore(storeId);
            var currentPaymentMethodConfig =
                storeData.GetPaymentMethodConfig<CashPaymentMethodConfig>(CashCheckoutPlugin.CashPmid, handlers);
            if (currentPaymentMethodConfig == null)
                return false;

            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();
            var enabled = !excludeFilters.Match(CashCheckoutPlugin.CashPmid);

            return enabled;
        }
        catch
        {
            return false;
        }
    }
}
