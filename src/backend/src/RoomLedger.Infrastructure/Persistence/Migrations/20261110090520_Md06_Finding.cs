using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Md06_Finding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inspection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    move_in_photo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    move_out_photo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    phash_distance = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    bounding_box_json = table.Column<string>(type: "jsonb", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: true),
                    model_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    used_fallback = table.Column<bool>(type: "boolean", nullable: false),
                    is_manual = table.Column<bool>(type: "boolean", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_finding", x => x.id);
                    table.CheckConstraint("ck_finding_amount_positive", "amount IS NULL OR amount > 0");
                    table.CheckConstraint("ck_finding_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
                    table.ForeignKey(
                        name: "fk_finding_inspections_inspection_id",
                        column: x => x.inspection_id,
                        principalTable: "inspection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_finding_photos_move_in_photo_id",
                        column: x => x.move_in_photo_id,
                        principalTable: "photo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_finding_photos_move_out_photo_id",
                        column: x => x.move_out_photo_id,
                        principalTable: "photo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_finding_room_areas_room_area_id",
                        column: x => x.room_area_id,
                        principalTable: "room_area",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_finding_inspection_id",
                table: "finding",
                column: "inspection_id");

            migrationBuilder.CreateIndex(
                name: "ix_finding_move_in_photo_id",
                table: "finding",
                column: "move_in_photo_id");

            migrationBuilder.CreateIndex(
                name: "ix_finding_move_out_photo_id",
                table: "finding",
                column: "move_out_photo_id");

            migrationBuilder.CreateIndex(
                name: "ix_finding_room_area_id",
                table: "finding",
                column: "room_area_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finding");
        }
    }
}
