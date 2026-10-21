using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JabberAlertBridge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DirectUserRecipients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "room_jid",
                table: "outgoing_messages",
                newName: "recipient_jid");

            migrationBuilder.Sql("""
                UPDATE outgoing_messages
                SET status = 'dead',
                    dead_at = COALESCE(dead_at, now()),
                    locked_by = NULL,
                    lease_until = NULL,
                    last_error = left('legacy-room-message: ' || COALESCE(last_error, 'manual-review-required'), 512)
                WHERE status IN ('pending', 'processing', 'dead');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_outgoing_messages_recipient_jid_sequence",
                table: "outgoing_messages",
                columns: new[] { "recipient_jid", "sequence" },
                filter: "status IN ('pending', 'processing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE outgoing_messages
                SET status = 'dead',
                    dead_at = now(),
                    locked_by = NULL,
                    lease_until = NULL,
                    last_error = left('direct-user-message: ' || COALESCE(last_error, 'manual-review-required'), 512)
                WHERE status IN ('pending', 'processing');
                """);

            migrationBuilder.DropIndex(
                name: "IX_outgoing_messages_recipient_jid_sequence",
                table: "outgoing_messages");

            migrationBuilder.RenameColumn(
                name: "recipient_jid",
                table: "outgoing_messages",
                newName: "room_jid");
        }
    }
}
