using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobspect.Modules.Analytics.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "analytics");

            migrationBuilder.CreateTable(
                name: "application_facts",
                schema: "analytics",
                columns: table => new
                {
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    company_id = table.Column<Guid>(type: "uuid", nullable: true),
                    applied_date = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    work_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    stage_entered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stage_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    campaign_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_response_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reached_screening_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reached_interview_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reached_offer_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_interview_scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_facts", x => x.application_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_facts_owner_id_campaign_id",
                schema: "analytics",
                table: "application_facts",
                columns: new[] { "owner_id", "campaign_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_facts",
                schema: "analytics");
        }
    }
}
