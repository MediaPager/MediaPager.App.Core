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
    public DbSet<CatalogItemTag> CatalogItemTags => Set<CatalogItemTag>();
    public DbSet<CatalogItemRating> CatalogItemRatings => Set<CatalogItemRating>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();
}
