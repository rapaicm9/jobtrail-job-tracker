using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobspect.Modules.Applications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFieldDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "custom_fields",
                schema: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    label_normalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, computedColumnSql: "lower(label)", stored: true),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    options = table.Column<string[]>(type: "text[]", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_custom_fields", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_custom_fields_owner_id_created_at_id",
                schema: "applications",
                table: "custom_fields",
                columns: new[] { "owner_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_custom_fields_owner_id_label_normalized",
                schema: "applications",
                table: "custom_fields",
                columns: new[] { "owner_id", "label_normalized" },
                unique: true,
                filter: "NOT is_archived");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_fields",
                schema: "applications");
        }
    }
}
