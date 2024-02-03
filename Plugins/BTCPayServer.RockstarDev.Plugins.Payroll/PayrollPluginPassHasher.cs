using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using Microsoft.AspNetCore.Identity;

namespace BTCPayServer.RockstarDev.Plugins.Payroll
{
    public class PayrollPluginPassHasher
    {
        private PasswordHasher<string> _hasher = new PasswordHasher<string>();

        public bool IsValidPassword(PayrollUser user, string providedPassword)
        {
            var res = _hasher.VerifyHashedPassword(user.Id, user.Password, providedPassword);
            return res == PasswordVerificationResult.Success || res == PasswordVerificationResult.SuccessRehashNeeded;
            // TODO: Handle the case of rehashing needed
        }

        public string HashPassword(string userId, string password)
        {
            return _hasher.HashPassword(userId, password);
        }
    }
}
