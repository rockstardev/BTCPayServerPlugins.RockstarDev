using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;

public class ValidationResult
{
    private readonly Dictionary<string, string> _errors = new();

    public bool IsValid => !_errors.Any();
    public IReadOnlyDictionary<string, string> Errors => _errors;

    public void AddError(string key, string message)
    {
        if (!_errors.ContainsKey(key)) // Avoid duplicate errors
            _errors.Add(key, message);
    }

    public void ApplyToModelState(ModelStateDictionary modelState)
    {
        foreach (var error in _errors)
            modelState.AddModelError(error.Key, error.Value);
    }
}
