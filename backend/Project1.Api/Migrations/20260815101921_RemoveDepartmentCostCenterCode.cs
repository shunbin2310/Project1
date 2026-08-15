using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Project1.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDepartmentCostCenterCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostCenterCode",
                table: "Departments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CostCenterCode",
                table: "Departments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }
    }
}
