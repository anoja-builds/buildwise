using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BuildWise.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityInspectionAndNonConformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inspections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeliveryId = table.Column<int>(type: "integer", nullable: false),
                    InspectorUserId = table.Column<int>(type: "integer", nullable: false),
                    InspectionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false, defaultValue: "Pending"),
                    OverallDecision = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                    table.ForeignKey(
                        name: "FK_inspections_users_InspectorUserId",
                        column: x => x.InspectorUserId,
                        principalTable: "users",
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
                    DeliveryItemId = table.Column<int>(type: "integer", nullable: false),
                    Condition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AcceptedQuantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    RejectedQuantity = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inspection_items_delivery_items_DeliveryItemId",
                        column: x => x.DeliveryItemId,
                        principalTable: "delivery_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inspection_items_inspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "inspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "non_conformances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InspectionItemId = table.Column<int>(type: "integer", nullable: false),
                    IssueDescription = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    CorrectiveAction = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false, defaultValue: "Open"),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
                name: "IX_inspection_items_DeliveryItemId",
                table: "inspection_items",
                column: "DeliveryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_items_InspectionId",
                table: "inspection_items",
                column: "InspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_items_InspectionId_DeliveryItemId",
                table: "inspection_items",
                columns: new[] { "InspectionId", "DeliveryItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inspections_DeliveryId",
                table: "inspections",
                column: "DeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_inspections_InspectionDate",
                table: "inspections",
                column: "InspectionDate");

            migrationBuilder.CreateIndex(
                name: "IX_inspections_InspectorUserId",
                table: "inspections",
                column: "InspectorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_inspections_Status",
                table: "inspections",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_non_conformances_InspectionItemId",
                table: "non_conformances",
                column: "InspectionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_non_conformances_Severity",
                table: "non_conformances",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_non_conformances_Status",
                table: "non_conformances",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "non_conformances");

            migrationBuilder.DropTable(
                name: "inspection_items");

            migrationBuilder.DropTable(
                name: "inspections");
        }
    }
}
