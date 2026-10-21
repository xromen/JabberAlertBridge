using JabberAlertBridge.Models;
using Microsoft.EntityFrameworkCore;

namespace JabberAlertBridge.Infrastructure.Persistence;

public sealed class AlertDbContext(DbContextOptions<AlertDbContext> options) : DbContext(options)
{
    public DbSet<OutgoingMessage> OutgoingMessages => Set<OutgoingMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutgoingMessageConfiguration());
    }
}
