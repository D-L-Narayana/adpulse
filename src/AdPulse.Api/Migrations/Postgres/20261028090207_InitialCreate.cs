using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AdPulse.Api.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "adpulse");

            migrationBuilder.CreateTable(
                name: "Campaigns",
                schema: "adpulse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DailyBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "adpulse",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Imports",
                schema: "adpulse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    RowsTotal = table.Column<int>(type: "integer", nullable: false),
                    RowsImported = table.Column<int>(type: "integer", nullable: false),
                    RowsSkipped = table.Column<int>(type: "integer", nullable: false),
                    CampaignsCreated = table.Column<int>(type: "integer", nullable: false),
                    MetricsInserted = table.Column<int>(type: "integer", nullable: false),
                    MetricsUpdated = table.Column<int>(type: "integer", nullable: false),
                    WarningsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Imports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "adpulse",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    ApiKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApiKeyPrefix = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyMetrics",
                schema: "adpulse",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Clicks = table.Column<long>(type: "bigint", nullable: false),
                    Conversions = table.Column<long>(type: "bigint", nullable: false),
                    Spend = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyMetrics_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalSchema: "adpulse",
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_TenantId_Name",
                schema: "adpulse",
                table: "Campaigns",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_TenantId_Status",
                schema: "adpulse",
                table: "Campaigns",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyMetrics_CampaignId_Date",
                schema: "adpulse",
                table: "DailyMetrics",
                columns: new[] { "CampaignId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyMetrics_TenantId_Date",
                schema: "adpulse",
                table: "DailyMetrics",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_TenantId_Key",
                schema: "adpulse",
                table: "IdempotencyRecords",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Imports_TenantId_CreatedUtc",
                schema: "adpulse",
                table: "Imports",
                columns: new[] { "TenantId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ApiKeyHash",
                schema: "adpulse",
                table: "Tenants",
                column: "ApiKeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                schema: "adpulse",
                table: "Tenants",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyMetrics",
                schema: "adpulse");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "adpulse");

            migrationBuilder.DropTable(
                name: "Imports",
                schema: "adpulse");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "adpulse");

            migrationBuilder.DropTable(
                name: "Campaigns",
                schema: "adpulse");
        }
    }
}
