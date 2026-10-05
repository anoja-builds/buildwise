using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildWise.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierPortalRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SupplierId",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.InsertData(
                table: "roles",
                columns: new[] { "Id", "Name" },
                values: new object[] { 10, "Supplier" });

            migrationBuilder.CreateIndex(
                name: "IX_users_SupplierId",
                table: "users",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_users_suppliers_SupplierId",
                table: "users",
                column: "SupplierId",
                principalTable: "suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_users_suppliers_SupplierId",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_SupplierId",
                table: "users");

            migrationBuilder.DeleteData(
                table: "roles",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "users");
        }
    }
}
