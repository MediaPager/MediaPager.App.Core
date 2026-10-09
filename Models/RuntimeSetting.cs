using System.ComponentModel.DataAnnotations;

namespace MediaPager.App.Core.Models;

public sealed class RuntimeSetting
{
    [Key]
    public required string Key { get; set; }
    public string Value { get; set; } = "";
}
