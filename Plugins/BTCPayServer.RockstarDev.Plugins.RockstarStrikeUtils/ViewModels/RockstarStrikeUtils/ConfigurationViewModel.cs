using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.RockstarDev.Plugins.RockstarStrikeUtils.ViewModels.RockstarStrikeUtils;

public class ConfigurationViewModel
{
    [Required]
    public string StrikeApiKey { get; set; }
}