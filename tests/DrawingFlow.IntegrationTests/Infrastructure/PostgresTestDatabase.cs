using Npgsql;

namespace DrawingFlow.IntegrationTests.Infrastructure;

/// <summary>
/// Временная база PostgreSQL на dev-кластере (scripts/dev-postgres.sh). Создаётся на фикстуру, удаляется после тестов.
/// Тесты транзакций, ограничений и индексов намеренно выполняются на настоящей СУБД.
/// </summary>
public sealed class PostgresTestDatabase : IAsyncLifetime
{
    public static string AdminConnectionString =>
        Environment.GetEnvironmentVariable("DRAWINGFLOW_TEST_PG")
        ?? "Host=localhost;Port=5433;Database=postgres;Username=drawingflow;Password=drawingflow";

    public string DatabaseName { get; } = "drawingflow_test_" + Guid.NewGuid().ToString("N")[..12];

    public string ConnectionString => new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = DatabaseName }.ConnectionString;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "PostgreSQL для интеграционных тестов недоступен. Запустите scripts/dev-postgres.sh start " +
                "или задайте DRAWINGFLOW_TEST_PG (строка подключения с правом CREATEDB).", ex);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{DatabaseName}\" TEMPLATE template0 ENCODING 'UTF8'";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}
