using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace JabberAlertBridge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outgoing_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    room_jid = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dead_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outgoing_messages", x => x.id);
                    table.CheckConstraint("ck_message_attempts", "attempts >= 0");
                    table.CheckConstraint("ck_message_status", "status IN ('pending', 'processing', 'sent', 'dead')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_outgoing_active",
                table: "outgoing_messages",
                column: "sequence",
                unique: true,
                filter: "status IN ('pending', 'processing')");

            migrationBuilder.CreateIndex(
                name: "IX_outgoing_messages_dead_at",
                table: "outgoing_messages",
                column: "dead_at",
                filter: "status = 'dead'");

            migrationBuilder.CreateIndex(
                name: "IX_outgoing_messages_lease_until",
                table: "outgoing_messages",
                column: "lease_until",
                filter: "status = 'processing'");

            migrationBuilder.CreateIndex(
                name: "IX_outgoing_messages_sent_at",
                table: "outgoing_messages",
                column: "sent_at",
                filter: "status = 'sent'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outgoing_messages");
        }
    }
}
