using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace MediaPager.App.Core.Models;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : IdentityDbContext<AppUser>(options)
{
	public DbSet<RegistrationInvite> RegistrationInvites => Set<RegistrationInvite>();
    public DbSet<RuntimeSetting> RuntimeSettings => Set<RuntimeSetting>();
    public DbSet<MediaType> MediaTypes => Set<MediaType>();
    public DbSet<CatalogType> CatalogTypes => Set<CatalogType>();
    public DbSet<Catalog> Catalogs => Set<Catalog>();
    public DbSet<CatalogOrder> CatalogOrders => Set<CatalogOrder>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<CatalogItemMusicArtistCredit> CatalogItemMusicArtistCredits => Set<CatalogItemMusicArtistCredit>();
    public DbSet<CatalogItemTag> CatalogItemTags => Set<CatalogItemTag>();
    public DbSet<CatalogItemRating> CatalogItemRatings => Set<CatalogItemRating>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<CatalogItemMusicArtistCredit>()
            .HasOne(credit => credit.CatalogItem)
            .WithMany(item => item.MusicArtistCredits)
            .HasForeignKey(credit => credit.CatalogItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<CatalogItemMusicArtistCredit>()
            .HasOne(credit => credit.ArtistCatalogItem)
            .WithMany()
            .HasForeignKey(credit => credit.ArtistCatalogItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
