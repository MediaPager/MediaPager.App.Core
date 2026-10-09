using Microsoft.EntityFrameworkCore;

namespace MediaPager.App.Core.Models;

[Index(nameof(Slug), IsUnique = true)]
public sealed class CatalogType
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public int MediaTypeId { get; set; }
    public MediaType? MediaType { get; set; }
}
