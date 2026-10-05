using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildWise.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionQualityChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DefectsCheck",
                table: "inspections",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MoistureCheck",
                table: "inspections",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PackagingCheck",
                table: "inspections",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "QuantityCheck",
                table: "inspections",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VisualConditionCheck",
                table: "inspections",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefectsCheck",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "MoistureCheck",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "PackagingCheck",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "QuantityCheck",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "VisualConditionCheck",
                table: "inspections");
        }
    }
}
