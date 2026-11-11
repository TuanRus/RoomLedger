using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Md04_PhotoCaptureSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "capture_source",
                table: "photo",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "GALLERY"); // ảnh cũ không rõ nguồn → GALLERY
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "capture_source",
                table: "photo");
        }
    }
}
