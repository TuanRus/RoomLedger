using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Md06_InspectionFinalPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "final_period",
                table: "inspection",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            // Biên bản trả phòng đã mở trước bản này: kỳ = tháng giờ Việt Nam (UTC+7) của lúc mở (quyết định A3)
            migrationBuilder.Sql("""
                UPDATE inspection
                SET final_period = to_char((created_at AT TIME ZONE 'UTC') + interval '7 hours', 'YYYY-MM')
                WHERE type = 'MOVE_OUT' AND final_period IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "final_period",
                table: "inspection");
        }
    }
}
