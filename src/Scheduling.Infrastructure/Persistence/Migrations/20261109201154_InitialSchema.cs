using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduling.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Necessária para combinar igualdade (uuid) e sobreposição (tstzrange) no mesmo índice GiST (research R-01).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.ForeignKey(
                        name: "fk_customers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "providers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    category = table.Column<string>(type: "text", nullable: false),
                    contact_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address_street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address_complement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address_district = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_state = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    address_postal_code = table.Column<string>(type: "character(8)", fixedLength: true, maxLength: 8, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    auto_confirm = table.Column<bool>(type: "boolean", nullable: false),
                    slot_step_minutes = table.Column<short>(type: "smallint", nullable: false),
                    buffer_minutes = table.Column<short>(type: "smallint", nullable: false),
                    min_booking_notice_minutes = table.Column<int>(type: "integer", nullable: false),
                    booking_horizon_days = table.Column<short>(type: "smallint", nullable: false),
                    min_cancellation_notice_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_providers", x => x.id);
                    table.ForeignKey(
                        name: "fk_providers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provider_schedule_exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    end_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_schedule_exceptions", x => x.id);
                    table.CheckConstraint("ck_schedule_exceptions_dates", "end_date >= start_date");
                    table.CheckConstraint("ck_schedule_exceptions_times", "(start_time IS NULL AND end_time IS NULL) OR (start_time < end_time)");
                    table.ForeignKey(
                        name: "fk_provider_schedule_exceptions_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_working_hours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_working_hours", x => x.id);
                    table.CheckConstraint("ck_working_hours_range", "start_time < end_time AND day_of_week BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "fk_provider_working_hours_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    duration_minutes = table.Column<short>(type: "smallint", nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                    table.CheckConstraint("ck_services_duration", "duration_minutes BETWEEN 5 AND 720 AND duration_minutes % 5 = 0");
                    table.CheckConstraint("ck_services_price", "price >= 0 AND price <= 99999.99");
                    table.ForeignKey(
                        name: "fk_services_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    service_duration_minutes = table.Column<short>(type: "smallint", nullable: false),
                    service_price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    blocked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    customer_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointments_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_providers_provider_id",
                        column: x => x.provider_id,
                        principalTable: "providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_appointments_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointment_status_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "text", nullable: true),
                    to_status = table.Column<string>(type: "text", nullable: false),
                    changed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_by_role = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointment_status_changes_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_appointment_status_changes_users_changed_by_user_id",
                        column: x => x.changed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    attempts = table.Column<short>(type: "smallint", nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_attempts", "attempts BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "fk_notifications_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notifications_users_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointment_status_changes_changed_by_user_id",
                table: "appointment_status_changes",
                column: "changed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_status_changes_appointment",
                table: "appointment_status_changes",
                column: "appointment_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_customer_start",
                table: "appointments",
                columns: new[] { "customer_id", "start_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_provider_start",
                table: "appointments",
                columns: new[] { "provider_id", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_service",
                table: "appointments",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_status_start",
                table: "appointments",
                columns: new[] { "status", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ux_appointments_idempotency",
                table: "appointments",
                columns: new[] { "customer_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_customers_user_id",
                table: "customers",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_dispatch",
                table: "notifications",
                columns: new[] { "status", "next_attempt_at" },
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient",
                table: "notifications",
                columns: new[] { "recipient_user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_notifications_reminder",
                table: "notifications",
                columns: new[] { "appointment_id", "recipient_user_id" },
                unique: true,
                filter: "type = 'AppointmentReminder'");

            migrationBuilder.CreateIndex(
                name: "ix_exceptions_provider_dates",
                table: "provider_schedule_exceptions",
                columns: new[] { "provider_id", "start_date", "end_date" });

            migrationBuilder.CreateIndex(
                name: "ix_working_hours_provider_day",
                table: "provider_working_hours",
                columns: new[] { "provider_id", "day_of_week" });

            migrationBuilder.CreateIndex(
                name: "ix_providers_address_city",
                table: "providers",
                column: "address_city");

            migrationBuilder.CreateIndex(
                name: "ix_providers_category_active",
                table: "providers",
                columns: new[] { "category", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_providers_display_name",
                table: "providers",
                column: "display_name");

            migrationBuilder.CreateIndex(
                name: "ux_providers_user_id",
                table: "providers",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_services_provider",
                table: "services",
                column: "provider_id");

            migrationBuilder.CreateIndex(
                name: "ux_users_email",
                table: "users",
                column: "email",
                unique: true);

            // Nome de serviço único por prestador, sem distinção de maiúsculas (data-model.md).
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_services_provider_name ON services (provider_id, lower(name));");

            // Invariantes de tempo do agendamento.
            migrationBuilder.Sql("ALTER TABLE appointments ADD CONSTRAINT ck_appointments_time CHECK (end_at > start_at AND blocked_until >= end_at);");

            // Garantia final contra overbooking (FR-017/FR-018, SC-001): avaliada atomicamente pelo banco em INSERT/UPDATE.
            migrationBuilder.Sql("""
                ALTER TABLE appointments ADD CONSTRAINT ex_appointments_provider_overlap
                  EXCLUDE USING gist (provider_id WITH =, tstzrange(start_at, blocked_until, '[)') WITH &&)
                  WHERE (status IN ('Pending', 'Confirmed'));
                """);
            migrationBuilder.Sql("""
                ALTER TABLE appointments ADD CONSTRAINT ex_appointments_customer_overlap
                  EXCLUDE USING gist (customer_id WITH =, tstzrange(start_at, end_at, '[)') WITH &&)
                  WHERE (status IN ('Pending', 'Confirmed'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ex_appointments_customer_overlap;");
            migrationBuilder.Sql("ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ex_appointments_provider_overlap;");
            migrationBuilder.Sql("ALTER TABLE appointments DROP CONSTRAINT IF EXISTS ck_appointments_time;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_services_provider_name;");

            migrationBuilder.DropTable(
                name: "appointment_status_changes");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "provider_schedule_exceptions");

            migrationBuilder.DropTable(
                name: "provider_working_hours");

            migrationBuilder.DropTable(
                name: "appointments");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "services");

            migrationBuilder.DropTable(
                name: "providers");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
