using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTrail.Modules.Applications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignNaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name_normalized",
                schema: "applications",
                table: "campaigns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                computedColumnSql: "lower(name)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_owner_id_created_at_id",
                schema: "applications",
                table: "campaigns",
                columns: new[] { "owner_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_owner_id_name_normalized",
                schema: "applications",
                table: "campaigns",
                columns: new[] { "owner_id", "name_normalized" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_campaigns_owner_id_created_at_id",
                schema: "applications",
                table: "campaigns");

            migrationBuilder.DropIndex(
                name: "ix_campaigns_owner_id_name_normalized",
                schema: "applications",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "name_normalized",
                schema: "applications",
                table: "campaigns");
        }
    }
}
