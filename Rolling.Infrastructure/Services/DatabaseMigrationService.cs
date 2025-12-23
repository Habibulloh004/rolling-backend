using Microsoft.Extensions.Logging;
using Npgsql;

namespace Rolling.Infrastructure.Services;

public sealed class DatabaseMigrationService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseMigrationService> _logger;
    private readonly string _migrationsPath;

    public DatabaseMigrationService(
        string connectionString,
        ILogger<DatabaseMigrationService> logger,
        string migrationsPath)
    {
        _connectionString = connectionString;
        _logger = logger;
        _migrationsPath = migrationsPath;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting database migrations...");

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Ensure migrations tracking table exists
            await EnsureMigrationsTableAsync(connection, cancellationToken);

            // Get all migration files
            var migrationFiles = Directory.GetFiles(_migrationsPath, "*.sql")
                .OrderBy(f => Path.GetFileName(f))
                .ToList();

            if (migrationFiles.Count == 0)
            {
                _logger.LogInformation("No migration files found");
                return;
            }

            var appliedCount = 0;
            foreach (var migrationFile in migrationFiles)
            {
                var migrationName = Path.GetFileName(migrationFile);

                // Skip migrations table creation file
                if (migrationName.StartsWith("00_"))
                    continue;

                if (await IsMigrationAppliedAsync(connection, migrationName, cancellationToken))
                {
                    _logger.LogDebug("Migration {MigrationName} already applied, skipping", migrationName);
                    continue;
                }

                _logger.LogInformation("Applying migration: {MigrationName}", migrationName);

                var sql = await File.ReadAllTextAsync(migrationFile, cancellationToken);

                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                try
                {
                    await using var command = new NpgsqlCommand(sql, connection, transaction);
                    command.CommandTimeout = 300; // 5 minutes
                    await command.ExecuteNonQueryAsync(cancellationToken);

                    // Record migration
                    await RecordMigrationAsync(connection, transaction, migrationName, cancellationToken);

                    await transaction.CommitAsync(cancellationToken);
                    appliedCount++;
                    _logger.LogInformation("Migration {MigrationName} applied successfully", migrationName);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogError(ex, "Failed to apply migration {MigrationName}", migrationName);
                    throw;
                }
            }

            if (appliedCount > 0)
            {
                _logger.LogInformation("Database migrations completed. Applied {Count} migration(s)", appliedCount);
            }
            else
            {
                _logger.LogInformation("Database is up to date");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed");
            throw;
        }
    }

    private async Task EnsureMigrationsTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS schema_migrations (
                id SERIAL PRIMARY KEY,
                migration_name VARCHAR(255) NOT NULL UNIQUE,
                applied_at TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_migration_name ON schema_migrations(migration_name);
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> IsMigrationAppliedAsync(
        NpgsqlConnection connection,
        string migrationName,
        CancellationToken cancellationToken)
    {
        var sql = "SELECT COUNT(*) FROM schema_migrations WHERE migration_name = @name";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", migrationName);

        var count = (long?)await command.ExecuteScalarAsync(cancellationToken);
        return count > 0;
    }

    private async Task RecordMigrationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string migrationName,
        CancellationToken cancellationToken)
    {
        var sql = "INSERT INTO schema_migrations (migration_name) VALUES (@name)";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("name", migrationName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
