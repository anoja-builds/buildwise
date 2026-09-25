using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildWise.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillQualityNcrLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE non_conformances AS n
                SET "DeliveryId" = i."DeliveryId",
                    "MaterialId" = ii."MaterialId",
                    "QuantityAffected" = ii."RejectedQuantity"
                FROM inspection_items AS ii
                INNER JOIN inspections AS i ON i."Id" = ii."InspectionId"
                WHERE n."InspectionItemId" = ii."Id"
                  AND (n."DeliveryId" = 0 OR n."MaterialId" = 0 OR n."QuantityAffected" = 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
