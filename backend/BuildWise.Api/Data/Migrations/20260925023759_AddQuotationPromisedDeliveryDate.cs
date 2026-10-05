using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildWise.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotationPromisedDeliveryDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "PromisedDeliveryDate",
                table: "quotations",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_quotations_MaterialRequestId_PromisedDeliveryDate",
                table: "quotations",
                columns: new[] { "MaterialRequestId", "PromisedDeliveryDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_quotations_MaterialRequestId_PromisedDeliveryDate",
                table: "quotations");

            migrationBuilder.DropColumn(
                name: "PromisedDeliveryDate",
                table: "quotations");
        }
    }
}
