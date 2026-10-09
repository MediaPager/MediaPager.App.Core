using Microsoft.EntityFrameworkCore;

namespace MediaPager.App.Core.Models;

[Index(nameof(Slug), IsUnique = true)]
public sealed class MediaType
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string Description { get; set; } = "";
}
