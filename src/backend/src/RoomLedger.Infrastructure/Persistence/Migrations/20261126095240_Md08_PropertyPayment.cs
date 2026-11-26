using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Md08_PropertyPayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "account_holder",
                table: "property",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "account_number",
                table: "property",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                table: "property",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "qr_key",
                table: "property",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "qr_pending_key",
                table: "property",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "qr_status",
                table: "property",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transfer_note_template",
                table: "property",
                type: "text",
                nullable: true);
            // xmin là cột hệ thống của PostgreSQL (concurrency token của Property): không tạo cột
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "account_holder",
                table: "property");

            migrationBuilder.DropColumn(
                name: "account_number",
                table: "property");

            migrationBuilder.DropColumn(
                name: "bank_name",
                table: "property");

            migrationBuilder.DropColumn(
                name: "qr_key",
                table: "property");

            migrationBuilder.DropColumn(
                name: "qr_pending_key",
                table: "property");

            migrationBuilder.DropColumn(
                name: "qr_status",
                table: "property");

            migrationBuilder.DropColumn(
                name: "transfer_note_template",
                table: "property");
        }
    }
}
