using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;

public class DashboardViewModel
{
    [Required]
    public string StrikeApiKey { get; set; }
}