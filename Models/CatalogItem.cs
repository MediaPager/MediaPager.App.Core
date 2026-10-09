using Microsoft.EntityFrameworkCore;

namespace MediaPager.App.Core.Models;

// One row per media entry in a catalog: a movie, TV show/season/episode, podcast episode,
// audio track, audiobook chapter, book, etc. The media type mirrors the owning catalog's
// derived type (Catalog → CatalogType → MediaType) so per-item queries stay index-friendly.
// ParentId builds the hierarchy (show → seasons → episodes, album → tracks, book → chapters).
[Index(nameof(CatalogId))]
[Index(nameof(CatalogId), nameof(MediaTypeId))]
[Index(nameof(MediaTypeId))]
[Index(nameof(ParentId))]
[Index(nameof(Title))]
[Index(nameof(Kind))]
[Index(nameof(StoragePath), IsUnique = true)]
[Index(nameof(ExternalId))]
public sealed class CatalogItem
{
    public int Id { get; set; }
    public int CatalogId { get; set; }
    public Catalog? Catalog { get; set; }
    public int MediaTypeId { get; set; }
    public MediaType? MediaType { get; set; }

    // Optional parent for hierarchical items (TV show owns seasons/episodes, an album owns
    // tracks, a book owns chapters). Direct children = items whose ParentId == this Id.
    public int? ParentId { get; set; }
    public CatalogItem? Parent { get; set; }

    public required string Title { get; set; }
    public string? SortTitle { get; set; }
    public string Kind { get; set; } = "item"; // movie, show, season, episode, track, chapter, ...
    public string? Overview { get; set; }

    // Where the entry lives on disk (file or folder path under its catalog).
    public string? StoragePath { get; set; }
    public long? SizeBytes { get; set; }
    public long? DurationSeconds { get; set; }

    public int? Year { get; set; }
    public string? SeasonNumber { get; set; }
    public string? EpisodeNumber { get; set; }

    // Full display metadata (IMDb-style). Imported items can be seeded from TMDB; scanned
    // entries start empty and are filled in by hand or by future metadata scrapers.
    public string? OriginalTitle { get; set; }
    public DateTime? OriginalAvailableAt { get; set; }
    public string? ContentRating { get; set; }
    public string? OriginalLanguage { get; set; }

    // Shared/editor-set rating (TMDB's vote_average by default for imported entries; starts at
    // null for scans). Each user keeps a personal rating in CatalogItemRating.
    public double? Rating { get; set; }

    // Custom artwork: any source (TMDB/IMDb URLs or uploaded local files). Items without
    // an external id (home videos, scans) can still get posters/backdrops this way.
    public string? ImageUrl { get; set; }
    public string? BackdropUrl { get; set; }

    // Optional external identifier (e.g. TMDB/IMDb id) used to match/metadata enrich items.
    public string? ExternalId { get; set; }

    public List<CatalogItemTag> Tags { get; set; } = [];
    public List<CatalogItemRating> Ratings { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ModifiedAt { get; set; }
}
