using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BuildWise.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class MergeComponent2AndDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "QuotationId",
                table: "purchase_orders",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "purchase_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SupplierId",
                table: "purchase_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "QuotationItemId",
                table: "purchase_order_items",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "purchase_order_items",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "MaterialId",
                table: "purchase_order_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "purchase_order_items",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<int>(
                name: "MaterialRequestId",
                table: "agent_workflows",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "DeliveryId",
                table: "agent_workflows",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseOrderId",
                table: "agent_workflows",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "agent_workflow_steps",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "agent_workflow_steps",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "deliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PurchaseOrderId = table.Column<int>(type: "integer", nullable: false),
                    ActualArrivalDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedByUserId = table.Column<int>(type: "integer", nullable: true),
                    DeliveryReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    PhotographicEvidenceUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_deliveries_purchase_orders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deliveries_users_ReceivedByUserId",
                        column: x => x.ReceivedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DeliverySchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PurchaseOrderId = table.Column<int>(type: "integer", nullable: false),
                    ScheduledDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScheduledTimeSlot = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliverySchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliverySchedules_purchase_orders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "purchase_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeliveryId = table.Column<int>(type: "integer", nullable: false),
                    PurchaseOrderItemId = table.Column<int>(type: "integer", nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    DamagedQuantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_delivery_items_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_delivery_items_purchase_order_items_PurchaseOrderItemId",
                        column: x => x.PurchaseOrderItemId,
                        principalTable: "purchase_order_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryEvidences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeliveryId = table.Column<int>(type: "integer", nullable: false),
                    DeliveryItemId = table.Column<int>(type: "integer", nullable: true),
                    FileUrl = table.Column<string>(type: "text", nullable: false),
                    FileType = table.Column<string>(type: "text", nullable: true),
                    UploadedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryEvidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryEvidences_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeliveryEvidences_delivery_items_DeliveryItemId",
                        column: x => x.DeliveryItemId,
                        principalTable: "delivery_items",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryEvidences_users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DeliveryIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeliveryId = table.Column<int>(type: "integer", nullable: false),
                    DeliveryItemId = table.Column<int>(type: "integer", nullable: true),
                    IssueType = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Resolution = table.Column<string>(type: "text", nullable: true),
                    ReportedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ReportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryIssues_deliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "deliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeliveryIssues_delivery_items_DeliveryItemId",
                        column: x => x.DeliveryItemId,
                        principalTable: "delivery_items",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryIssues_users_ReportedByUserId",
                        column: x => x.ReportedByUserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_ProjectId",
                table: "purchase_orders",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_SupplierId",
                table: "purchase_orders",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_items_MaterialId",
                table: "purchase_order_items",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_agent_workflows_DeliveryId",
                table: "agent_workflows",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_DeliveryReference",
                table: "deliveries",
                column: "DeliveryReference");

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_PurchaseOrderId",
                table: "deliveries",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_ReceivedByUserId",
                table: "deliveries",
                column: "ReceivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_Status",
                table: "deliveries",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_items_DeliveryId_PurchaseOrderItemId",
                table: "delivery_items",
                columns: new[] { "DeliveryId", "PurchaseOrderItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_items_PurchaseOrderItemId",
                table: "delivery_items",
                column: "PurchaseOrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryEvidences_DeliveryId",
                table: "DeliveryEvidences",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryEvidences_DeliveryItemId",
                table: "DeliveryEvidences",
                column: "DeliveryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryEvidences_UploadedByUserId",
                table: "DeliveryEvidences",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryIssues_DeliveryId",
                table: "DeliveryIssues",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryIssues_DeliveryItemId",
                table: "DeliveryIssues",
                column: "DeliveryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryIssues_ReportedByUserId",
                table: "DeliveryIssues",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_PurchaseOrderId",
                table: "DeliverySchedules",
                column: "PurchaseOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_agent_workflows_deliveries_DeliveryId",
                table: "agent_workflows",
                column: "DeliveryId",
                principalTable: "deliveries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_agent_workflows_users_InitiatedByUserId",
                table: "agent_workflows",
                column: "InitiatedByUserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_order_items_materials_MaterialId",
                table: "purchase_order_items",
                column: "MaterialId",
                principalTable: "materials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_orders_projects_ProjectId",
                table: "purchase_orders",
                column: "ProjectId",
                principalTable: "projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_purchase_orders_suppliers_SupplierId",
                table: "purchase_orders",
                column: "SupplierId",
                principalTable: "suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_agent_workflows_deliveries_DeliveryId",
                table: "agent_workflows");

            migrationBuilder.DropForeignKey(
                name: "FK_agent_workflows_users_InitiatedByUserId",
                table: "agent_workflows");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_order_items_materials_MaterialId",
                table: "purchase_order_items");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_orders_projects_ProjectId",
                table: "purchase_orders");

            migrationBuilder.DropForeignKey(
                name: "FK_purchase_orders_suppliers_SupplierId",
                table: "purchase_orders");

            migrationBuilder.DropTable(
                name: "DeliveryEvidences");

            migrationBuilder.DropTable(
                name: "DeliveryIssues");

            migrationBuilder.DropTable(
                name: "DeliverySchedules");

            migrationBuilder.DropTable(
                name: "delivery_items");

            migrationBuilder.DropTable(
                name: "deliveries");

            migrationBuilder.DropIndex(
                name: "IX_purchase_orders_ProjectId",
                table: "purchase_orders");

            migrationBuilder.DropIndex(
                name: "IX_purchase_orders_SupplierId",
                table: "purchase_orders");

            migrationBuilder.DropIndex(
                name: "IX_purchase_order_items_MaterialId",
                table: "purchase_order_items");

            migrationBuilder.DropIndex(
                name: "IX_agent_workflows_DeliveryId",
                table: "agent_workflows");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "MaterialId",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "DeliveryId",
                table: "agent_workflows");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                table: "agent_workflows");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "agent_workflow_steps");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "agent_workflow_steps");

            migrationBuilder.AlterColumn<int>(
                name: "QuotationId",
                table: "purchase_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "QuotationItemId",
                table: "purchase_order_items",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "MaterialRequestId",
                table: "agent_workflows",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
