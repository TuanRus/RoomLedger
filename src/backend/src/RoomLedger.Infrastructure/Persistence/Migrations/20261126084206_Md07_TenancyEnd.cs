using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Md07_TenancyEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_meter_reading_room_id_period_kind",
                table: "meter_reading");

            migrationBuilder.AddColumn<DateOnly>(
                name: "end_date",
                table: "tenancy",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "end_kind",
                table: "tenancy",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ended_at",
                table: "tenancy",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "last_rent",
                table: "tenancy",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "move_target_room_id",
                table: "tenancy",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "next_tenancy_id",
                table: "tenancy",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenancy_move_target_room_id",
                table: "tenancy",
                column: "move_target_room_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenancy_next_tenancy_id",
                table: "tenancy",
                column: "next_tenancy_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenancy_end_kind_target",
                table: "tenancy",
                sql: "(end_kind IS NOT DISTINCT FROM 'MOVE') = (move_target_room_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenancy_last_rent",
                table: "tenancy",
                sql: "last_rent IS NULL OR last_rent >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_meter_reading_room_id_period",
                table: "meter_reading",
                columns: new[] { "room_id", "period" });

            migrationBuilder.CreateIndex(
                name: "ux_meter_reading_tenancy_id_period_kind",
                table: "meter_reading",
                columns: new[] { "tenancy_id", "period", "kind" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_tenancy_room_move_target_room_id",
                table: "tenancy",
                column: "move_target_room_id",
                principalTable: "room",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_tenancy_tenancy_next_tenancy_id",
                table: "tenancy",
                column: "next_tenancy_id",
                principalTable: "tenancy",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tenancy_room_move_target_room_id",
                table: "tenancy");

            migrationBuilder.DropForeignKey(
                name: "fk_tenancy_tenancy_next_tenancy_id",
                table: "tenancy");

            migrationBuilder.DropIndex(
                name: "ix_tenancy_move_target_room_id",
                table: "tenancy");

            migrationBuilder.DropIndex(
                name: "ix_tenancy_next_tenancy_id",
                table: "tenancy");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenancy_end_kind_target",
                table: "tenancy");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenancy_last_rent",
                table: "tenancy");

            migrationBuilder.DropIndex(
                name: "ix_meter_reading_room_id_period",
                table: "meter_reading");

            migrationBuilder.DropIndex(
                name: "ux_meter_reading_tenancy_id_period_kind",
                table: "meter_reading");

            // Hai người thuê cùng phòng trong một tháng có hai chỉ số cùng (room_id, period, kind): giữ bản mới nhất
            // (không xóa bản đã dùng cho hóa đơn) để tạo lại được unique index cũ. Nếu vẫn còn trùng thì Down báo lỗi, không bỏ qua im lặng.
            migrationBuilder.Sql(@"
                DELETE FROM meter_reading m USING meter_reading n
                WHERE m.room_id = n.room_id AND m.period = n.period AND m.kind = n.kind AND m.id <> n.id
                  AND (m.created_at < n.created_at OR (m.created_at = n.created_at AND m.id < n.id))
                  AND NOT EXISTS (SELECT 1 FROM invoice i WHERE i.electricity_reading_id = m.id OR i.water_reading_id = m.id);");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "tenancy");

            migrationBuilder.DropColumn(
                name: "end_kind",
                table: "tenancy");

            migrationBuilder.DropColumn(
                name: "ended_at",
                table: "tenancy");

            migrationBuilder.DropColumn(
                name: "last_rent",
                table: "tenancy");

            migrationBuilder.DropColumn(
                name: "move_target_room_id",
                table: "tenancy");

            migrationBuilder.DropColumn(
                name: "next_tenancy_id",
                table: "tenancy");

            migrationBuilder.CreateIndex(
                name: "ux_meter_reading_room_id_period_kind",
                table: "meter_reading",
                columns: new[] { "room_id", "period", "kind" },
                unique: true);
        }
    }
}
