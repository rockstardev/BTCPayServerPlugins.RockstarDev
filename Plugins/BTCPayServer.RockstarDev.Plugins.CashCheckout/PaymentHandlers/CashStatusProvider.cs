using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.RockstarDev.Plugins.CashCheckout.PaymentHandlers;

public class CashStatusProvider(StoreRepository storeRepository,
    CashCheckoutConfigurationItem cashMethod,
    PaymentMethodHandlerDictionary handlers)
{
    public async Task<bool> CashEnabled(string? storeId)
    {
        try
        {
            var storeData = await storeRepository.FindStore(storeId);
            var currentPaymentMethodConfig =
                storeData.GetPaymentMethodConfig<CashPaymentMethodConfig>(cashMethod.GetPaymentMethodId(), handlers);
            if (currentPaymentMethodConfig == null)
                return false;
            
            var excludeFilters = storeData.GetStoreBlob().GetExcludedPaymentMethods();
            var enabled = !excludeFilters.Match(cashMethod.GetPaymentMethodId());

            return enabled;
        }
        catch
        {
            return false;
        }
    }
}