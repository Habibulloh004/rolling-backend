using System;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Rolling.Infrastructure.Persistence.Postgres;
using StackExchange.Redis;
using Microsoft.Extensions.Hosting;

namespace Rolling.Infrastructure;

public static class InfrastructureInitializationExtensions
{
    /// <summary>
    /// Ensures that required infrastructure services are available at startup.
    /// Currently this validates the Redis connection by issuing a ping.
    /// </summary>
    /// <param name="services">The application's root service provider.</param>
    public static void EnsureInfrastructure(this IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var hostEnvironment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var connectionMultiplexer = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // This will throw if Redis is not reachable, failing fast on startup.
        var database = connectionMultiplexer.GetDatabase();
        _ = database.Ping();

        // Ensure the PostgreSQL database exists and apply any pending migrations.
        dbContext.Database.Migrate();
        ApplySqlMigrations(dbContext, hostEnvironment.ContentRootPath);
    }

    private static void ApplySqlMigrations(AppDbContext dbContext, string contentRootPath)
    {
        var migrationsDir = Path.Combine(contentRootPath, "..", "migrations");
        if (!Directory.Exists(migrationsDir))
        {
            return;
        }

        var migrations = Directory.GetFiles(migrationsDir, "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();

        if (migrations.Length == 0)
        {
            return;
        }

        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;
        if (shouldCloseConnection)
        {
            connection.Open();
        }

        try
        {
            using (var ensureHistory = connection.CreateCommand())
            {
                ensureHistory.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS migrations_applied (
                        name TEXT PRIMARY KEY,
                        applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                    );
                    """;
                ensureHistory.ExecuteNonQuery();
            }

            var appliedMigrations = new HashSet<string>(StringComparer.Ordinal);
            using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT name FROM migrations_applied";
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    appliedMigrations.Add(reader.GetString(0));
                }
            }

            foreach (var migrationPath in migrations)
            {
                var migrationName = Path.GetFileName(migrationPath);
                if (appliedMigrations.Contains(migrationName))
                {
                    continue;
                }

                var sql = File.ReadAllText(migrationPath);
                if (string.IsNullOrWhiteSpace(sql))
                {
                    appliedMigrations.Add(migrationName);
                    continue;
                }

                using (var migrateCommand = connection.CreateCommand())
                {
                    migrateCommand.CommandText = sql;
                    migrateCommand.ExecuteNonQuery();
                }

                using (var insert = connection.CreateCommand())
                {
                    insert.CommandText = "INSERT INTO migrations_applied (name) VALUES (@name)";
                    var parameter = insert.CreateParameter();
                    parameter.ParameterName = "@name";
                    parameter.Value = migrationName;
                    insert.Parameters.Add(parameter);
                    insert.ExecuteNonQuery();
                }
            }
        }
        finally
        {
            if (shouldCloseConnection)
            {
                connection.Close();
            }
        }
    }
}
