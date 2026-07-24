using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTrail.Modules.Applications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activity_log",
                schema: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    from_stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    to_stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    transition_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_activity_log_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "applications",
                        principalTable: "applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activity_log_application_id",
                schema: "applications",
                table: "activity_log",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_activity_log_owner_id",
                schema: "applications",
                table: "activity_log",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_log",
                schema: "applications");
        }
    }
}
