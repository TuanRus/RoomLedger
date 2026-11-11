using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Md05_TenancyOpeningReadings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "opening_electricity",
                table: "tenancy",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "opening_water",
                table: "tenancy",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenancy_opening_electricity",
                table: "tenancy",
                sql: "opening_electricity IS NULL OR opening_electricity >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenancy_opening_water",
                table: "tenancy",
                sql: "opening_water IS NULL OR opening_water >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tenancy_opening_electricity",
                table: "tenancy");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenancy_opening_water",
                table: "tenancy");

            migrationBuilder.DropColumn(
                name: "opening_electricity",
                table: "tenancy");

            migrationBuilder.DropColumn(
                name: "opening_water",
                table: "tenancy");
        }
    }
}
