using Microsoft.EntityFrameworkCore;

namespace MediaPager.App.Core.Models;

// A single user's personal rating of a catalog item (0–10). One row per (user, item);
// the item's shared Rating field holds the official/TMDB rating.
[Index(nameof(UserId), nameof(CatalogItemId), IsUnique = true)]
public sealed class CatalogItemRating
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public AppUser? User { get; set; }
    public int CatalogItemId { get; set; }
    public CatalogItem? CatalogItem { get; set; }

    public double Rating { get; set; }
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}