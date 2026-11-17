using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Md06_Settlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settlement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenancy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inspection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deposit = table.Column<long>(type: "bigint", nullable: false),
                    deductions_total = table.Column<long>(type: "bigint", nullable: false),
                    unpaid_invoices_total = table.Column<long>(type: "bigint", nullable: false),
                    refund = table.Column<long>(type: "bigint", nullable: false),
                    pdf_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settled_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settlement", x => x.id);
                    table.CheckConstraint("ck_settlement_amounts", "deposit >= 0 AND deductions_total >= 0 AND unpaid_invoices_total >= 0");
                    table.CheckConstraint("ck_settlement_refund", "refund = deposit - deductions_total - unpaid_invoices_total");
                    table.ForeignKey(
                        name: "fk_settlement_app_user_settled_by",
                        column: x => x.settled_by,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlement_inspection_inspection_id",
                        column: x => x.inspection_id,
                        principalTable: "inspection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_settlement_tenancies_tenancy_id",
                        column: x => x.tenancy_id,
                        principalTable: "tenancy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_settlement_inspection_id",
                table: "settlement",
                column: "inspection_id");

            migrationBuilder.CreateIndex(
                name: "ix_settlement_settled_by",
                table: "settlement",
                column: "settled_by");

            migrationBuilder.CreateIndex(
                name: "ux_settlement_tenancy_id",
                table: "settlement",
                column: "tenancy_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "settlement");
        }
    }
}
