using JabberAlertBridge.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JabberAlertBridge.Infrastructure.Persistence;

public sealed class OutgoingMessageConfiguration : IEntityTypeConfiguration<OutgoingMessage>
{
    public void Configure(EntityTypeBuilder<OutgoingMessage> builder)
    {
        builder.ToTable("outgoing_messages", table =>
        {
            table.HasCheckConstraint("ck_message_status", "status IN ('pending', 'processing', 'sent', 'dead')");
            table.HasCheckConstraint("ck_message_attempts", "attempts >= 0");
        });
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id");
        builder.Property(message => message.Sequence).HasColumnName("sequence").UseIdentityAlwaysColumn();
        builder.Property(message => message.Source).HasColumnName("source").HasMaxLength(32).IsRequired();
        builder.Property(message => message.RoomJid).HasColumnName("room_jid").HasMaxLength(512).IsRequired();
        builder.Property(message => message.Content).HasColumnName("content").IsRequired();
        builder.Property(message => message.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(message => message.Attempts).HasColumnName("attempts");
        builder.Property(message => message.CreatedAt).HasColumnName("created_at");
        builder.Property(message => message.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(message => message.LockedBy).HasColumnName("locked_by").HasMaxLength(128);
        builder.Property(message => message.LeaseUntil).HasColumnName("lease_until");
        builder.Property(message => message.SentAt).HasColumnName("sent_at");
        builder.Property(message => message.DeadAt).HasColumnName("dead_at");
        builder.Property(message => message.LastError).HasColumnName("last_error").HasMaxLength(512);
        builder.HasIndex(message => message.Sequence).IsUnique();
        builder.HasIndex(message => message.Sequence).HasDatabaseName("ix_outgoing_active")
            .HasFilter("status IN ('pending', 'processing')");
        builder.HasIndex(message => message.LeaseUntil).HasFilter("status = 'processing'");
        builder.HasIndex(message => message.SentAt).HasFilter("status = 'sent'");
        builder.HasIndex(message => message.DeadAt).HasFilter("status = 'dead'");
    }
}
