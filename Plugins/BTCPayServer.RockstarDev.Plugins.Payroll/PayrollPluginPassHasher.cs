using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.RockstarDev.Plugins.Payroll;

public class PayrollPluginPassHasher
{
    private readonly PasswordHasher<string> _hasher = new();

    public bool IsValidPassword(PayrollUser user, string providedPassword)
    {
        var res = _hasher.VerifyHashedPassword(user.Id, user.Password, providedPassword);
        return res is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
        // TODO: Handle the case of rehashing needed
    }

    public string HashPassword(string userId, string password)
    {
        return _hasher.HashPassword(userId, password);
    }
}