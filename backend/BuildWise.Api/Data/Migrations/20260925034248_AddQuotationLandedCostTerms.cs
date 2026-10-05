using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildWise.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationLandedCostTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentTerms",
                table: "quotations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TransportCharge",
                table: "quotations",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentTerms",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "TransportCharge",
                table: "quotations");
        }
    }
}
