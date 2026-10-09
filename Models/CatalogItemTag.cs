using Microsoft.EntityFrameworkCore;

namespace MediaPager.App.Core.Models;

// IMDb-style metadata for a catalog item: Genre, Country, Writer, Director, Producer.
// Each (item, type, value) is unique so re-saving a tag set doesn't duplicate rows.
[Index(nameof(CatalogItemId), nameof(Type), nameof(Value), IsUnique = true)]
public sealed class CatalogItemTag
{
    public int Id { get; set; }
    public int CatalogItemId { get; set; }
    public CatalogItem? CatalogItem { get; set; }

    // One of: genre | country | writer | director | producer
    public required string Type { get; set; }
    public required string Value { get; set; }
}