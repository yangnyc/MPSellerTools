using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPSellerTools.Infrastructure.Tenants.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BulkJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ChannelAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Total = table.Column<int>(type: "int", nullable: false),
                    Processed = table.Column<int>(type: "int", nullable: false),
                    Succeeded = table.Column<int>(type: "int", nullable: false),
                    Failed = table.Column<int>(type: "int", nullable: false),
                    CancelRequested = table.Column<bool>(type: "bit", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ErrorsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HeartbeatAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BulkJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BulkJobs_ChannelAccounts_ChannelAccountId",
                        column: x => x.ChannelAccountId,
                        principalTable: "ChannelAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BulkJobs_ChannelAccountId",
                table: "BulkJobs",
                column: "ChannelAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BulkJobs_Status_CreatedAtUtc",
                table: "BulkJobs",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BulkJobs");
        }
    }
}
