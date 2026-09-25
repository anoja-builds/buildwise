using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BuildWise.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialRequestRevisionHistoryAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "material_requests",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<DateOnly>(
                name: "RequestDate",
                table: "material_requests",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE");

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "material_requests",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "RevisionOfRequestId",
                table: "material_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteNotes",
                table: "material_requests",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "material_request_items",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "material_request_items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "RequiredDate",
                table: "material_request_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "material_request_items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "material_request_history",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaterialRequestId = table.Column<int>(type: "integer", nullable: false),
                    ChangedByUserId = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_request_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_material_request_history_material_requests_MaterialRequestId",
                        column: x => x.MaterialRequestId,
                        principalTable: "material_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_material_request_history_users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_material_requests_RevisionOfRequestId",
                table: "material_requests",
                column: "RevisionOfRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_material_request_history_ChangedByUserId",
                table: "material_request_history",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_material_request_history_MaterialRequestId_CreatedAt",
                table: "material_request_history",
                columns: new[] { "MaterialRequestId", "CreatedAt" });

        // Preserve legacy/demo requests whose old RequestedByUserId was not a real user.
        // They are attributed to the seeded Site Engineer before the FK is added.
        migrationBuilder.Sql("""
            UPDATE material_requests AS mr
            SET "RequestedByUserId" = (SELECT u."Id" FROM users AS u WHERE u."Email" = 'site.engineer@buildwise.demo' LIMIT 1)
            WHERE NOT EXISTS (SELECT 1 FROM users AS u WHERE u."Id" = mr."RequestedByUserId")
              AND EXISTS (SELECT 1 FROM users AS u WHERE u."Email" = 'site.engineer@buildwise.demo');
            """);

            migrationBuilder.AddForeignKey(
                name: "FK_material_requests_material_requests_RevisionOfRequestId",
                table: "material_requests",
                column: "RevisionOfRequestId",
                principalTable: "material_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_material_requests_users_RequestedByUserId",
                table: "material_requests",
                column: "RequestedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_material_requests_material_requests_RevisionOfRequestId",
                table: "material_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_material_requests_users_RequestedByUserId",
                table: "material_requests");

            migrationBuilder.DropTable(
                name: "material_request_history");

            migrationBuilder.DropIndex(
                name: "IX_material_requests_RevisionOfRequestId",
                table: "material_requests");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "material_requests");

            migrationBuilder.DropColumn(
                name: "RequestDate",
                table: "material_requests");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "material_requests");

            migrationBuilder.DropColumn(
                name: "RevisionOfRequestId",
                table: "material_requests");

            migrationBuilder.DropColumn(
                name: "SiteNotes",
                table: "material_requests");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "material_request_items");

            migrationBuilder.DropColumn(
                name: "RequiredDate",
                table: "material_request_items");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "material_request_items");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "material_request_items",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);
        }
    }
}
