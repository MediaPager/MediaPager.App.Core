using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaPager.App.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogItemBackdropUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackdropUrl",
                table: "CatalogItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackdropUrl",
                table: "CatalogItems");
        }
    }
}
