using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediaPager.App.Core.Services;

/// <summary>ILibraryQuery over AuthDbContext. Registered as a singleton so it can sit in
/// (singleton) plugin constructors; it opens a scope per call so the scoped DbContext never
/// leaks into plugin lifetime.</summary>
public sealed class LibraryQuery(IServiceScopeFactory scopes) : ILibraryQuery
{
    public async Task<IReadOnlyList<LibraryItemHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        var needle = query.Trim().ToLowerInvariant();
        if (needle.Length == 0 || limit <= 0)
            return [];

        using var scope = scopes.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        // Top-level items only (shows, albums, movies, ...): type-ahead noise from
        // episodes/tracks is suppressed. Prefer prefix title matches, then rating.
        var rows = await database.CatalogItems.AsNoTracking()
            .Where(item => item.ParentId == null
                && (item.Title.ToLower().Contains(needle)
                    || (item.Overview != null && item.Overview.ToLower().Contains(needle))))
            .OrderByDescending(item => item.Title.ToLower().StartsWith(needle))
            .ThenByDescending(item => item.Rating)
            .ThenBy(item => item.Title)
            .Take(limit)
            .Select(item => new
            {
                item.Id,
                item.Title,
                item.Year,
                item.Rating,
                item.Overview,
                item.ImageUrl,
                item.BackdropUrl,
                item.Kind,
                CatalogItemId = item.Id,
                item.CatalogId,
                item.ExternalId,
                TypeSlug = item.MediaType != null ? item.MediaType.Slug : null
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new LibraryItemHit(
                $"local:{row.Id}",
                MapKind(row.Kind, row.TypeSlug),
                row.Title,
                row.Year,
                row.Rating,
                row.Overview,
                row.ImageUrl ?? row.BackdropUrl,
                row.CatalogItemId,
                row.CatalogId,
                row.ExternalId))
            .ToList();
    }

    // CatalogItem.Kind is a free string ("movie", "show", "season", "episode", "track",
    // "chapter"); the owning catalog's MediaType slug ("video"/"audio"/"book") disambiguates
    // the shared words (episode: TV vs podcast; chapter: book vs audiobook).
    private static MediaKind MapKind(string kind, string? typeSlug) => kind.ToLowerInvariant() switch
    {
        "movie" => MediaKind.Movie,
        "show" or "season" => MediaKind.Tv,
        "podcast" => MediaKind.Podcast,
        "audiobook" => MediaKind.Audiobook,
        "book" => MediaKind.Book,
        "episode" => typeSlug == "audio" ? MediaKind.Podcast : MediaKind.Tv,
        "track" => MediaKind.Music,
        "chapter" => typeSlug == "audio" ? MediaKind.Audiobook : MediaKind.Book,
        _ => (typeSlug ?? "").ToLowerInvariant() switch
        {
            "audio" => MediaKind.Music,
            "book" => MediaKind.Book,
            _ => MediaKind.Movie
        }
    };
}
