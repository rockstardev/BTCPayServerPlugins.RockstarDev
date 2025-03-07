using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Logic;

public class RequiredIfAttribute(string dependentProperty, object targetValue) : ValidationAttribute
{
    protected override ValidationResult IsValid(object value, ValidationContext validationContext)
    {
        var property = validationContext.ObjectType.GetProperty(dependentProperty);
        if (property == null)
            return new ValidationResult($"Unknown property: {dependentProperty}");

        var dependentValue = property.GetValue(validationContext.ObjectInstance, null);
        if ((dependentValue == null && targetValue == null) || (dependentValue != null && dependentValue.Equals(targetValue)))
        {
            if (value == null || (value is string s && string.IsNullOrWhiteSpace(s)))
            {
                return new ValidationResult($"{validationContext.DisplayName} is required.");
            }
        }

        return ValidationResult.Success;
    }
}