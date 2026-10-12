using Microsoft.EntityFrameworkCore;

namespace MediaPager.App.Core.Models;

/// <summary>Ordered album-artist or track-artist credit for a music catalog item.</summary>
[Index(nameof(CatalogItemId), nameof(Role), nameof(Position), IsUnique = true)]
[Index(nameof(ArtistCatalogItemId), nameof(Role))]
public sealed class CatalogItemMusicArtistCredit
{
    public int Id { get; set; }

    public int CatalogItemId { get; set; }
    public CatalogItem? CatalogItem { get; set; }

    public int ArtistCatalogItemId { get; set; }
    public CatalogItem? ArtistCatalogItem { get; set; }

    // "artist" for recording credits; "album-artist" for release credits.
    public required string Role { get; set; }
    public int Position { get; set; }
}
