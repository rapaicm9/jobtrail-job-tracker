using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobspect.Modules.Notifications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "reminder_rules",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    days_after_applied = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminder_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tracked_applications",
                schema: "notifications",
                columns: table => new
                {
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_date = table.Column<DateOnly>(type: "date", nullable: true),
                    stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    stage_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracked_applications", x => x.application_id);
                });

            migrationBuilder.CreateTable(
                name: "reminders",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "Pending"),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interview_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    subject_date = table.Column<DateOnly>(type: "date", nullable: true),
                    source_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminders", x => x.id);
                    table.ForeignKey(
                        name: "fk_reminders_reminder_rules_rule_id",
                        column: x => x.rule_id,
                        principalSchema: "notifications",
                        principalTable: "reminder_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "reminder_deliveries",
                schema: "notifications",
                columns: table => new
                {
                    reminder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminder_deliveries", x => new { x.reminder_id, x.channel });
                    table.ForeignKey(
                        name: "fk_reminder_deliveries_reminders_reminder_id",
                        column: x => x.reminder_id,
                        principalSchema: "notifications",
                        principalTable: "reminders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reminder_rules_owner_id",
                schema: "notifications",
                table: "reminder_rules",
                column: "owner_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reminders_application_id",
                schema: "notifications",
                table: "reminders",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminders_application_id_interview_id_kind",
                schema: "notifications",
                table: "reminders",
                columns: new[] { "application_id", "interview_id", "kind" },
                unique: true,
                filter: "state = 'Pending'")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_reminders_due_at",
                schema: "notifications",
                table: "reminders",
                column: "due_at",
                filter: "state = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_reminders_owner_id_due_at_id",
                schema: "notifications",
                table: "reminders",
                columns: new[] { "owner_id", "due_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_reminders_rule_id",
                schema: "notifications",
                table: "reminders",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracked_applications_owner_id_applied_date",
                schema: "notifications",
                table: "tracked_applications",
                columns: new[] { "owner_id", "applied_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reminder_deliveries",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "tracked_applications",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "reminders",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "reminder_rules",
                schema: "notifications");
        }
    }
}
