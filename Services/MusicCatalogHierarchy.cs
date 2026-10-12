using Microsoft.EntityFrameworkCore;

namespace MediaPager.App.Core.Services;

/// <summary>Maintains Artist → Album nodes when the last track or credit is removed.</summary>
public static class MusicCatalogHierarchy
{
    public static async Task PruneEmptyAncestorsAsync(
        AuthDbContext database,
        IEnumerable<int> candidateIds,
        CancellationToken cancellationToken = default)
    {
        foreach (var candidateId in candidateIds.Distinct())
            await PruneEmptyAncestorAsync(database, candidateId, cancellationToken);
    }

    public static Task PruneEmptyAncestorsAsync(
        AuthDbContext database,
        int? candidateId,
        CancellationToken cancellationToken = default) =>
        candidateId is int id
            ? PruneEmptyAncestorAsync(database, id, cancellationToken)
            : Task.CompletedTask;

    private static async Task PruneEmptyAncestorAsync(
        AuthDbContext database,
        int candidateId,
        CancellationToken cancellationToken)
    {
        int? currentId = candidateId;
        while (currentId is int id)
        {
            var item = await database.CatalogItems
                .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (item is null || item.Kind is not ("album" or "artist")) return;

            var hasChildren = await database.CatalogItems
                .AnyAsync(child => child.ParentId == item.Id, cancellationToken);
            var hasArtistCredits = item.Kind == "artist" && await database.CatalogItemMusicArtistCredits
                .AnyAsync(credit => credit.ArtistCatalogItemId == item.Id, cancellationToken);
            if (hasChildren || hasArtistCredits) return;

            currentId = item.ParentId;
            database.CatalogItems.Remove(item);
            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
