using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobspect.Modules.Applications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFieldValuesIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_applications_custom_field_values",
                schema: "applications",
                table: "applications",
                column: "custom_field_values")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_applications_custom_field_values",
                schema: "applications",
                table: "applications");
        }
    }
}
