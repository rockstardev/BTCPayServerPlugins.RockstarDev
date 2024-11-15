using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Logic;

public class RequiredIfAttribute : ValidationAttribute
{
    private readonly string _dependentProperty;
    private readonly object _targetValue;

    public RequiredIfAttribute(string dependentProperty, object targetValue)
    {
        _dependentProperty = dependentProperty;
        _targetValue = targetValue;
    }

    protected override ValidationResult IsValid(object value, ValidationContext validationContext)
    {
        var property = validationContext.ObjectType.GetProperty(_dependentProperty);
        if (property == null)
            return new ValidationResult($"Unknown property: {_dependentProperty}");

        var dependentValue = property.GetValue(validationContext.ObjectInstance, null);
        if ((dependentValue == null && _targetValue == null) || (dependentValue != null && dependentValue.Equals(_targetValue)))
        {
            if (value == null || (value is string s && string.IsNullOrWhiteSpace(s)))
            {
                return new ValidationResult($"{validationContext.DisplayName} is required.");
            }
        }

        return ValidationResult.Success;
    }
}