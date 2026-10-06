using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPSellerTools.Infrastructure.Tenants.Migrations
{
    /// <inheritdoc />
    public partial class AddEbayIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EbayOrderId",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EbayConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Environment = table.Column<int>(type: "int", nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClientSecretProtected = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    RuName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RefreshTokenProtected = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    RefreshTokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConnectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PendingState = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastOrderSyncAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastProductSyncAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastSyncError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EbayConnections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_EbayOrderId",
                table: "Orders",
                column: "EbayOrderId",
                unique: true,
                filter: "[EbayOrderId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EbayConnections");

            migrationBuilder.DropIndex(
                name: "IX_Orders_EbayOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "EbayOrderId",
                table: "Orders");
        }
    }
}
