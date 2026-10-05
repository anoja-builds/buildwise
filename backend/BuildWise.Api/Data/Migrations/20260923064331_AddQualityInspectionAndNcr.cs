using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BuildWise.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityInspectionAndNcr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_deliveries_users_ReceivedByUserId",
                table: "deliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_delivery_items_purchase_order_items_PurchaseOrderItemId",
                table: "delivery_items");

            migrationBuilder.DropIndex(
                name: "IX_delivery_items_DeliveryId_PurchaseOrderItemId",
                table: "delivery_items");

            migrationBuilder.DropIndex(
                name: "IX_deliveries_ReceivedByUserId",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "delivery_items");

            migrationBuilder.DropColumn(
                name: "ActualArrivalDate",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "PhotographicEvidenceUrl",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ReceivedAt",
                table: "deliveries");

            migrationBuilder.RenameColumn(
                name: "PurchaseOrderItemId",
                table: "delivery_items",
                newName: "MaterialId");

            migrationBuilder.RenameIndex(
                name: "IX_delivery_items_PurchaseOrderItemId",
                table: "delivery_items",
                newName: "IX_delivery_items_MaterialId");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "deliveries",
                newName: "DeliveredAt");

            migrationBuilder.AlterColumn<int>(
                name: "ReceivedByUserId",
                table: "deliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DeliveryReference",
                table: "deliveries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "Approvals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MaterialRequestId = table.Column<int>(type: "integer", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Comments = table.Column<string>(type: "text", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Approvals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Approvals_material_requests_MaterialRequestId",
                        column: x => x.MaterialRequestId,
                        principalTable: "material_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Approvals_users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inspections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeliveryId = table.Column<int>(type: "integer", nullable: false),
                    InspectorUserId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OverallDecision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InspectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inspections_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inspection_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InspectionId = table.Column<int>(type: "integer", nullable: false),
                    MaterialId = table.Column<int>(type: "integer", nullable: false),
                    InspectedQuantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    AcceptedQuantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    RejectedQuantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inspection_items_inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inspection_items_materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "non_conformances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NcrNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InspectionItemId = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IssueDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CorrectiveActionPlan = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_non_conformances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_non_conformances_inspection_items_InspectionItemId",
                        column: x => x.InspectionItemId,
                        principalTable: "inspection_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_items_DeliveryId",
                table: "delivery_items",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_ApprovedByUserId",
                table: "Approvals",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Approvals_MaterialRequestId",
                table: "Approvals",
                column: "MaterialRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_items_InspectionId",
                table: "inspection_items",
                column: "InspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_items_MaterialId",
                table: "inspection_items",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_inspections_DeliveryId",
                table: "inspections",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_inspections_Status",
                table: "inspections",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_non_conformances_InspectionItemId",
                table: "non_conformances",
                column: "InspectionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_non_conformances_NcrNumber",
                table: "non_conformances",
                column: "NcrNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_non_conformances_Status",
                table: "non_conformances",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_delivery_items_materials_MaterialId",
                table: "delivery_items",
                column: "MaterialId",
                principalTable: "materials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_delivery_items_materials_MaterialId",
                table: "delivery_items");

            migrationBuilder.DropTable(
                name: "Approvals");

            migrationBuilder.DropTable(
                name: "non_conformances");

            migrationBuilder.DropTable(
                name: "inspection_items");

            migrationBuilder.DropTable(
                name: "inspections");

            migrationBuilder.DropIndex(
                name: "IX_delivery_items_DeliveryId",
                table: "delivery_items");

            migrationBuilder.RenameColumn(
                name: "MaterialId",
                table: "delivery_items",
                newName: "PurchaseOrderItemId");

            migrationBuilder.RenameIndex(
                name: "IX_delivery_items_MaterialId",
                table: "delivery_items",
                newName: "IX_delivery_items_PurchaseOrderItemId");

            migrationBuilder.RenameColumn(
                name: "DeliveredAt",
                table: "deliveries",
                newName: "UpdatedAt");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "delivery_items",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ReceivedByUserId",
                table: "deliveries",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "DeliveryReference",
                table: "deliveries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualArrivalDate",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhotographicEvidenceUrl",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAt",
                table: "deliveries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_items_DeliveryId_PurchaseOrderItemId",
                table: "delivery_items",
                columns: new[] { "DeliveryId", "PurchaseOrderItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_ReceivedByUserId",
                table: "deliveries",
                column: "ReceivedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_deliveries_users_ReceivedByUserId",
                table: "deliveries",
                column: "ReceivedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_delivery_items_purchase_order_items_PurchaseOrderItemId",
                table: "delivery_items",
                column: "PurchaseOrderItemId",
                principalTable: "purchase_order_items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
