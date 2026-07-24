using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTrail.Modules.Applications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationBuiltInFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "application_deadline",
                schema: "applications",
                table: "applications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "applied_date",
                schema: "applications",
                table: "applications",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE");

            migrationBuilder.AddColumn<Guid>(
                name: "campaign_id",
                schema: "applications",
                table: "applications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                schema: "applications",
                table: "applications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "compensation_amount",
                schema: "applications",
                table: "applications",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "compensation_currency",
                schema: "applications",
                table: "applications",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cover_letter_label",
                schema: "applications",
                table: "applications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cv_label",
                schema: "applications",
                table: "applications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location",
                schema: "applications",
                table: "applications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "offer_decision_deadline",
                schema: "applications",
                table: "applications",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "posting_url",
                schema: "applications",
                table: "applications",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                schema: "applications",
                table: "applications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "applications",
                table: "applications",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "work_mode",
                schema: "applications",
                table: "applications",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_applications_campaign_id",
                schema: "applications",
                table: "applications",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_applications_company_id",
                schema: "applications",
                table: "applications",
                column: "company_id");

            migrationBuilder.AddForeignKey(
                name: "fk_applications_campaigns_campaign_id",
                schema: "applications",
                table: "applications",
                column: "campaign_id",
                principalSchema: "applications",
                principalTable: "campaigns",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_applications_companies_company_id",
                schema: "applications",
                table: "applications",
                column: "company_id",
                principalSchema: "applications",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_applications_campaigns_campaign_id",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropForeignKey(
                name: "fk_applications_companies_company_id",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropIndex(
                name: "ix_applications_campaign_id",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropIndex(
                name: "ix_applications_company_id",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "application_deadline",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "applied_date",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "campaign_id",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "company_id",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "compensation_amount",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "compensation_currency",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "cover_letter_label",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "cv_label",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "location",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "offer_decision_deadline",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "posting_url",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "role",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "work_mode",
                schema: "applications",
                table: "applications");
        }
    }
}
