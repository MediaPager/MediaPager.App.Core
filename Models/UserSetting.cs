using Microsoft.EntityFrameworkCore;

namespace MediaPager.App.Core.Models;

// A single user-scoped preference (e.g. "autoplay" = "true"), stored as a
// key/value row so future per-user settings don't need new columns. TV-only
// settings like autoplay live here; runtime/system settings stay in RuntimeSetting.
[Index(nameof(UserId), nameof(Key), IsUnique = true)]
public sealed class UserSetting
{
    public int Id { get; set; }

    public required string UserId { get; set; }

    public required string Key { get; set; }

    public string Value { get; set; } = "";
}