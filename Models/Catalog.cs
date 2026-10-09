namespace MediaPager.App.Core.Models;

public sealed class Catalog
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public int CatalogTypeId { get; set; }
    public CatalogType? CatalogType { get; set; }
    public string? Path { get; set; }
}
