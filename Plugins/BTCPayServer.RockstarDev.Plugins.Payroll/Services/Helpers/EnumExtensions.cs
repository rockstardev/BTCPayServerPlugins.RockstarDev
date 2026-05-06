using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace BTCPayServer.RockstarDev.Plugins.VendorPay.Services.Helpers;

public static class EnumExtensions
{
    public static string GetDisplayName(this Enum value)
    {
        var name = value.ToString();
        var member = value.GetType().GetField(name);
        if (member is null) return name;
        var display = member.GetCustomAttribute<DisplayAttribute>();
        return display?.Name ?? name;
    }
}
