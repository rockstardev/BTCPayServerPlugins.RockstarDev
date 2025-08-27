using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.RockstarDev.Plugins.CreditCheckout.PaymentHandlers;

public class CreditStatusProvider(
    StoreRepository storeRepository,
    PaymentMethodHandlerDictionary handlers)
{
    public async Task<bool> CreditEnabled(string storeId)
    {
        try
        {
            var storeData = await storeRepository.FindStore(storeId);
            var currentPaymentMethodConfig =
                storeData.GetPaymentMethodConfig<CreditPaymentMethodConfig>(CreditCheckoutPlugin.CreditPmid, handlers);
            if (currentPaymentMethodConfig == null)
                return false;

            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();
            var enabled = !excludeFilters.Match(CreditCheckoutPlugin.CreditPmid);

            return enabled;
        }
        catch
        {
            return false;
        }
    }
}
