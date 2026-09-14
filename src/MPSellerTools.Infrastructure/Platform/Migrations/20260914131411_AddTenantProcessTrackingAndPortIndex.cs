using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPSellerTools.Infrastructure.Platform.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantProcessTrackingAndPortIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationInstanceId",
                table: "Tenants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessId",
                table: "Tenants",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessStartTimeUtc",
                table: "Tenants",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Port",
                table: "Tenants",
                column: "Port",
                unique: true,
                filter: "[Port] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tenants_Port",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ApplicationInstanceId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ProcessId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ProcessStartTimeUtc",
                table: "Tenants");
        }
    }
}
