using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace MediaPager.App.Core.Models;

// A user's *personal* custom ordering of catalogs in the left nav. Rows only exist
// for catalogs the user has explicitly ordered; anything without a row falls back to
// creation order and is appended after the overridden ones.
[Index(nameof(UserId), nameof(CatalogId), IsUnique = true)]
public sealed class CatalogOrder
{
    public int Id { get; set; }

    public required string UserId { get; set; }
    [ForeignKey(nameof(UserId))]
    public AppUser? AppUser { get; set; }

    public int CatalogId { get; set; }
    [ForeignKey(nameof(CatalogId))]
    public Catalog? Catalog { get; set; }

    public int SortOrder { get; set; }
}