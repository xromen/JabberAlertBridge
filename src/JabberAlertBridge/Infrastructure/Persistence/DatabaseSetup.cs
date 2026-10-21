using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JabberAlertBridge.Infrastructure.Persistence;

public static class DatabaseSetup
{
    public static string GetConnectionString(IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("Postgres") ?? "";

        try
        {
            var connection = new NpgsqlConnectionStringBuilder(connectionString);

            if (string.IsNullOrWhiteSpace(connection.Host)
                || string.IsNullOrWhiteSpace(connection.Database)
                || string.IsNullOrWhiteSpace(connection.Username)
                || connection.Password == "CHANGE_ME")
            {
                throw new ArgumentException();
            }

            return connectionString;
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Задайте корректную ConnectionStrings:Postgres, без пароля CHANGE_ME.");
        }
    }

    public static async Task ApplyMigrationsAsync(string connectionString)
    {
        try
        {
            var options = new DbContextOptionsBuilder<AlertDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            await using var db = new AlertDbContext(options);
            await db.Database.MigrateAsync();

            Console.WriteLine("Миграции PostgreSQL применены.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Не удалось применить миграции: {exception.GetType().Name}");
            Environment.ExitCode = 1;
        }
    }
}
