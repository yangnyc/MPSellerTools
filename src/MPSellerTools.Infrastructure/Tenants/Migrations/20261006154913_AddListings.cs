using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPSellerTools.Infrastructure.Tenants.Migrations
{
    /// <inheritdoc />
    public partial class AddListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Listings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Marketplace = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    AvailableQuantity = table.Column<int>(type: "int", nullable: true),
                    SoldQuantity = table.Column<int>(type: "int", nullable: true),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Listings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Listings_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Listings_Channel_ExternalId",
                table: "Listings",
                columns: new[] { "Channel", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Listings_ProductId",
                table: "Listings",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Listings");
        }
    }
}
