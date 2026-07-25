using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTrail.Modules.Applications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                schema: "applications",
                table: "outbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // That default existed only to fill rows written before the column did.
            // Dropping it keeps an event with no owner from being quietly accepted:
            // every one of them is about somebody, and an erasure that cannot find
            // the rows it is meant to delete fails silently.
            migrationBuilder.Sql("ALTER TABLE applications.outbox ALTER COLUMN owner_id DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_owner_id",
                schema: "applications",
                table: "outbox",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_owner_id",
                schema: "applications",
                table: "outbox");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "applications",
                table: "outbox");
        }
    }
}
