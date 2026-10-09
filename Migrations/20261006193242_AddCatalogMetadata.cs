using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaPager.App.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentRating",
                table: "CatalogItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OriginalAvailableAt",
                table: "CatalogItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalLanguage",
                table: "CatalogItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalTitle",
                table: "CatalogItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Rating",
                table: "CatalogItems",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CatalogItemRatings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    CatalogItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Rating = table.Column<double>(type: "REAL", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogItemRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogItemRatings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CatalogItemRatings_CatalogItems_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "CatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogItemTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CatalogItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogItemTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogItemTags_CatalogItems_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "CatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItemRatings_CatalogItemId",
                table: "CatalogItemRatings",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItemRatings_UserId_CatalogItemId",
                table: "CatalogItemRatings",
                columns: new[] { "UserId", "CatalogItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItemTags_CatalogItemId_Type_Value",
                table: "CatalogItemTags",
                columns: new[] { "CatalogItemId", "Type", "Value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogItemRatings");

            migrationBuilder.DropTable(
                name: "CatalogItemTags");

            migrationBuilder.DropColumn(
                name: "ContentRating",
                table: "CatalogItems");

            migrationBuilder.DropColumn(
                name: "OriginalAvailableAt",
                table: "CatalogItems");

            migrationBuilder.DropColumn(
                name: "OriginalLanguage",
                table: "CatalogItems");

            migrationBuilder.DropColumn(
                name: "OriginalTitle",
                table: "CatalogItems");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "CatalogItems");
        }
    }
}
