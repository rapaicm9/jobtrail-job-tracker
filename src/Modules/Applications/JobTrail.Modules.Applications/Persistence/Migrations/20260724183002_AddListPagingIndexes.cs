using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTrail.Modules.Applications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddListPagingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_interviews_application_id",
                schema: "applications",
                table: "interviews");

            migrationBuilder.DropIndex(
                name: "ix_contacts_owner_id",
                schema: "applications",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_applications_owner_id",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropIndex(
                name: "ix_activity_log_application_id",
                schema: "applications",
                table: "activity_log");

            migrationBuilder.CreateIndex(
                name: "ix_interviews_application_id_scheduled_at_id",
                schema: "applications",
                table: "interviews",
                columns: new[] { "application_id", "scheduled_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_owner_id_name_id",
                schema: "applications",
                table: "contacts",
                columns: new[] { "owner_id", "name", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_applications_owner_id_applied_date_id",
                schema: "applications",
                table: "applications",
                columns: new[] { "owner_id", "applied_date", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_activity_log_application_id_created_at_id",
                schema: "applications",
                table: "activity_log",
                columns: new[] { "application_id", "created_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_interviews_application_id_scheduled_at_id",
                schema: "applications",
                table: "interviews");

            migrationBuilder.DropIndex(
                name: "ix_contacts_owner_id_name_id",
                schema: "applications",
                table: "contacts");

            migrationBuilder.DropIndex(
                name: "ix_applications_owner_id_applied_date_id",
                schema: "applications",
                table: "applications");

            migrationBuilder.DropIndex(
                name: "ix_activity_log_application_id_created_at_id",
                schema: "applications",
                table: "activity_log");

            migrationBuilder.CreateIndex(
                name: "ix_interviews_application_id",
                schema: "applications",
                table: "interviews",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_owner_id",
                schema: "applications",
                table: "contacts",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_applications_owner_id",
                schema: "applications",
                table: "applications",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_log_application_id",
                schema: "applications",
                table: "activity_log",
                column: "application_id");
        }
    }
}
