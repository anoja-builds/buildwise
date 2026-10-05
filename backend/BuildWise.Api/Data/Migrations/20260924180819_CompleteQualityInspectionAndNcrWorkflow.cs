using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BuildWise.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompleteQualityInspectionAndNcrWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "IssueDescription",
                table: "non_conformances",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "CorrectiveActionPlan",
                table: "non_conformances",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "non_conformances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryId",
                table: "non_conformances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaterialId",
                table: "non_conformances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityAffected",
                table: "non_conformances",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Resolution",
                table: "non_conformances",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "non_conformances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponsibleUserId",
                table: "non_conformances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                table: "non_conformances",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "non_conformances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewedByUserId",
                table: "non_conformances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierId",
                table: "non_conformances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "non_conformances",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<DateTime>(
                name: "InspectedAt",
                table: "inspections",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "inspections",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "InspectionCriteria",
                table: "inspections",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "inspections",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservedResult",
                table: "inspections",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "inspections",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.CreateTable(
                name: "inspection_evidences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InspectionId = table.Column<int>(type: "integer", nullable: false),
                    InspectionItemId = table.Column<int>(type: "integer", nullable: true),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "integer", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_evidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inspection_evidences_inspection_items_InspectionItemId",
                        column: x => x.InspectionItemId,
                        principalTable: "inspection_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_inspection_evidences_inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inspection_evidences_users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_non_conformances_DeliveryId",
                table: "non_conformances",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_non_conformances_MaterialId",
                table: "non_conformances",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_non_conformances_ResponsibleUserId",
                table: "non_conformances",
                column: "ResponsibleUserId");

            migrationBuilder.CreateIndex(
                name: "IX_non_conformances_ReviewedByUserId",
                table: "non_conformances",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_non_conformances_SupplierId",
                table: "non_conformances",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_evidences_InspectionId_UploadedAt",
                table: "inspection_evidences",
                columns: new[] { "InspectionId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_inspection_evidences_InspectionItemId",
                table: "inspection_evidences",
                column: "InspectionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_evidences_UploadedByUserId",
                table: "inspection_evidences",
                column: "UploadedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_non_conformances_users_ResponsibleUserId",
                table: "non_conformances",
                column: "ResponsibleUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_non_conformances_users_ReviewedByUserId",
                table: "non_conformances",
                column: "ReviewedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_non_conformances_users_ResponsibleUserId",
                table: "non_conformances");

            migrationBuilder.DropForeignKey(
                name: "FK_non_conformances_users_ReviewedByUserId",
                table: "non_conformances");

            migrationBuilder.DropTable(
                name: "inspection_evidences");

            migrationBuilder.DropIndex(
                name: "IX_non_conformances_DeliveryId",
                table: "non_conformances");

            migrationBuilder.DropIndex(
                name: "IX_non_conformances_MaterialId",
                table: "non_conformances");

            migrationBuilder.DropIndex(
                name: "IX_non_conformances_ResponsibleUserId",
                table: "non_conformances");

            migrationBuilder.DropIndex(
                name: "IX_non_conformances_ReviewedByUserId",
                table: "non_conformances");

            migrationBuilder.DropIndex(
                name: "IX_non_conformances_SupplierId",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "DeliveryId",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "MaterialId",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "QuantityAffected",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "Resolution",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "ResponsibleUserId",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "ReviewNotes",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "non_conformances");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "InspectionCriteria",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "ObservedResult",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "inspections");

            migrationBuilder.AlterColumn<string>(
                name: "IssueDescription",
                table: "non_conformances",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "CorrectiveActionPlan",
                table: "non_conformances",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<DateTime>(
                name: "InspectedAt",
                table: "inspections",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "CURRENT_TIMESTAMP");
        }
    }
}
