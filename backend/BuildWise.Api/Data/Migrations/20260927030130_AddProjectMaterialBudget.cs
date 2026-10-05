using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildWise.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectMaterialBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MaterialBudgetAmount",
                table: "projects",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaterialBudgetAmount",
                table: "projects");
        }
    }
}
