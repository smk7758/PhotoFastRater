using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoFastRater.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomPositionToExportTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomX",
                table: "ExportTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CustomY",
                table: "ExportTemplates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomX",
                table: "ExportTemplates");

            migrationBuilder.DropColumn(
                name: "CustomY",
                table: "ExportTemplates");
        }
    }
}
