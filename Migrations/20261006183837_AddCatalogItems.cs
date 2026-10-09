using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaPager.App.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CatalogId = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    SortTitle = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    StoragePath = table.Column<string>(type: "TEXT", nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    DurationSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    SeasonNumber = table.Column<string>(type: "TEXT", nullable: true),
                    EpisodeNumber = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogItems_CatalogItems_ParentId",
                        column: x => x.ParentId,
                        principalTable: "CatalogItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CatalogItems_Catalogs_CatalogId",
                        column: x => x.CatalogId,
                        principalTable: "Catalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CatalogItems_MediaTypes_MediaTypeId",
                        column: x => x.MediaTypeId,
                        principalTable: "MediaTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_CatalogId",
                table: "CatalogItems",
                column: "CatalogId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_CatalogId_MediaTypeId",
                table: "CatalogItems",
                columns: new[] { "CatalogId", "MediaTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_ExternalId",
                table: "CatalogItems",
                column: "ExternalId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_Kind",
                table: "CatalogItems",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_MediaTypeId",
                table: "CatalogItems",
                column: "MediaTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_ParentId",
                table: "CatalogItems",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_StoragePath",
                table: "CatalogItems",
                column: "StoragePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_Title",
                table: "CatalogItems",
                column: "Title");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogItems");
        }
    }
}
