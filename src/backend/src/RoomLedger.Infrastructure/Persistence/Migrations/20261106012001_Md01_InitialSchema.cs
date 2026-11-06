using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomLedger.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Md01_InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_event",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    detail_json = table.Column<string>(type: "jsonb", nullable: true),
                    trace_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "property",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    landlord_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    electricity_rate = table.Column<long>(type: "bigint", nullable: false),
                    water_rate = table.Column<long>(type: "bigint", nullable: false),
                    service_fee = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "room",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    landlord_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    monthly_rent = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room", x => x.id);
                    table.ForeignKey(
                        name: "fk_room_property_property_id",
                        column: x => x.property_id,
                        principalTable: "property",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_area",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    min_photos = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_area", x => x.id);
                    table.ForeignKey(
                        name: "fk_room_area_room_room_id",
                        column: x => x.room_id,
                        principalTable: "room",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenancy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    landlord_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenancy", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenancy_room_room_id",
                        column: x => x.room_id,
                        principalTable: "room",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inspection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenancy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    landlord_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspection", x => x.id);
                    table.ForeignKey(
                        name: "fk_inspection_tenancies_tenancy_id",
                        column: x => x.tenancy_id,
                        principalTable: "tenancy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invite",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenancy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character(44)", fixedLength: true, maxLength: 44, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    used_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invite", x => x.id);
                    table.ForeignKey(
                        name: "fk_invite_tenancies_tenancy_id",
                        column: x => x.tenancy_id,
                        principalTable: "tenancy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meter_reading",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenancy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ai_value = table.Column<long>(type: "bigint", nullable: true),
                    ai_confidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: true),
                    ai_raw_json = table.Column<string>(type: "jsonb", nullable: true),
                    value = table.Column<long>(type: "bigint", nullable: true),
                    review_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    meter_replaced = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    edited_by_landlord = table.Column<bool>(type: "boolean", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    photo_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    photo_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    photo_size = table.Column<long>(type: "bigint", nullable: true),
                    photo_sha256 = table.Column<string>(type: "character(44)", fixedLength: true, maxLength: 44, nullable: true),
                    photo_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    photo_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meter_reading", x => x.id);
                    table.ForeignKey(
                        name: "fk_meter_reading_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "room",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meter_reading_tenancies_tenancy_id",
                        column: x => x.tenancy_id,
                        principalTable: "tenancy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenancy_version",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenancy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ai_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ai_terms_json = table.Column<string>(type: "jsonb", nullable: true),
                    monthly_rent = table.Column<long>(type: "bigint", nullable: true),
                    deposit = table.Column<long>(type: "bigint", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    electricity_rate = table.Column<long>(type: "bigint", nullable: true),
                    water_rate = table.Column<long>(type: "bigint", nullable: true),
                    edited_fields = table.Column<List<string>>(type: "text[]", nullable: false),
                    landlord_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenancy_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenancy_version_tenancy_tenancy_id",
                        column: x => x.tenancy_id,
                        principalTable: "tenancy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "photo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inspection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploader_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(44)", fixedLength: true, maxLength: 44, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    incoming_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    derived_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    evidence_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    object_version_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    exif_taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    old_photo_warning = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_photo", x => x.id);
                    table.ForeignKey(
                        name: "fk_photo_inspection_inspection_id",
                        column: x => x.inspection_id,
                        principalTable: "inspection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_photo_room_areas_room_area_id",
                        column: x => x.room_area_id,
                        principalTable: "room_area",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenancy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    room_rent = table.Column<long>(type: "bigint", nullable: false),
                    electricity_reading_id = table.Column<Guid>(type: "uuid", nullable: false),
                    electricity_previous = table.Column<long>(type: "bigint", nullable: false),
                    electricity_current = table.Column<long>(type: "bigint", nullable: false),
                    electricity_rate = table.Column<long>(type: "bigint", nullable: false),
                    electricity_amount = table.Column<long>(type: "bigint", nullable: false),
                    water_reading_id = table.Column<Guid>(type: "uuid", nullable: false),
                    water_previous = table.Column<long>(type: "bigint", nullable: false),
                    water_current = table.Column<long>(type: "bigint", nullable: false),
                    water_rate = table.Column<long>(type: "bigint", nullable: false),
                    water_amount = table.Column<long>(type: "bigint", nullable: false),
                    service_fee = table.Column<long>(type: "bigint", nullable: false),
                    total = table.Column<long>(type: "bigint", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_meter_readings_electricity_reading_id",
                        column: x => x.electricity_reading_id,
                        principalTable: "meter_reading",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_meter_readings_water_reading_id",
                        column: x => x.water_reading_id,
                        principalTable: "meter_reading",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_tenancies_tenancy_id",
                        column: x => x.tenancy_id,
                        principalTable: "tenancy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenancy_file",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenancy_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(44)", fixedLength: true, maxLength: 44, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    incoming_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    evidence_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    object_version_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenancy_file", x => x.id);
                    table.ForeignKey(
                        name: "fk_tenancy_file_tenancy_versions_tenancy_version_id",
                        column: x => x.tenancy_version_id,
                        principalTable: "tenancy_version",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dispute",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenancy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    raised_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    resolution = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    response = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispute", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispute_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoice",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispute_tenancies_tenancy_id",
                        column: x => x.tenancy_id,
                        principalTable: "tenancy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_user_email",
                table: "app_user",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_actor_id",
                table: "audit_event",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_created_at",
                table: "audit_event",
                columns: new[] { "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_target_type_target_id",
                table: "audit_event",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dispute_invoice_id",
                table: "dispute",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispute_target_type_target_id",
                table: "dispute",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_dispute_tenancy_id",
                table: "dispute",
                column: "tenancy_id");

            migrationBuilder.CreateIndex(
                name: "ux_inspection_tenancy_id_type",
                table: "inspection",
                columns: new[] { "tenancy_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invite_tenancy_id",
                table: "invite",
                column: "tenancy_id");

            migrationBuilder.CreateIndex(
                name: "ux_invite_token_hash",
                table: "invite",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_electricity_reading_id",
                table: "invoice",
                column: "electricity_reading_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_period_status",
                table: "invoice",
                columns: new[] { "period", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_water_reading_id",
                table: "invoice",
                column: "water_reading_id");

            migrationBuilder.CreateIndex(
                name: "ux_invoice_tenancy_id_period",
                table: "invoice",
                columns: new[] { "tenancy_id", "period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_meter_reading_tenancy_id",
                table: "meter_reading",
                column: "tenancy_id");

            migrationBuilder.CreateIndex(
                name: "ux_meter_reading_room_id_period_kind",
                table: "meter_reading",
                columns: new[] { "room_id", "period", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_photo_inspection_id",
                table: "photo",
                column: "inspection_id");

            migrationBuilder.CreateIndex(
                name: "ix_photo_room_area_id",
                table: "photo",
                column: "room_area_id");

            migrationBuilder.CreateIndex(
                name: "ix_property_landlord_id_created_at",
                table: "property",
                columns: new[] { "landlord_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_room_landlord_id_created_at",
                table: "room",
                columns: new[] { "landlord_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_room_property_id_status",
                table: "room",
                columns: new[] { "property_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_room_property_id_code",
                table: "room",
                columns: new[] { "property_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_room_area_room_id",
                table: "room_area",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenancy_landlord_id",
                table: "tenancy",
                column: "landlord_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenancy_tenant_id",
                table: "tenancy",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_tenancy_room_id_open",
                table: "tenancy",
                column: "room_id",
                unique: true,
                filter: "status <> 'SETTLED'");

            migrationBuilder.CreateIndex(
                name: "ix_tenancy_file_tenancy_version_id",
                table: "tenancy_file",
                column: "tenancy_version_id");

            migrationBuilder.CreateIndex(
                name: "ux_tenancy_version_tenancy_id_number",
                table: "tenancy_version",
                columns: new[] { "tenancy_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_user");

            migrationBuilder.DropTable(
                name: "audit_event");

            migrationBuilder.DropTable(
                name: "dispute");

            migrationBuilder.DropTable(
                name: "invite");

            migrationBuilder.DropTable(
                name: "photo");

            migrationBuilder.DropTable(
                name: "tenancy_file");

            migrationBuilder.DropTable(
                name: "invoice");

            migrationBuilder.DropTable(
                name: "inspection");

            migrationBuilder.DropTable(
                name: "room_area");

            migrationBuilder.DropTable(
                name: "tenancy_version");

            migrationBuilder.DropTable(
                name: "meter_reading");

            migrationBuilder.DropTable(
                name: "tenancy");

            migrationBuilder.DropTable(
                name: "room");

            migrationBuilder.DropTable(
                name: "property");
        }
    }
}
