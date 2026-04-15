using System;
using System.IO;
using Npgsql;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;

namespace Sts2Extractor.Utilities;

internal sealed class DatabaseInitializer
{
    public static void InitializeSchema(CliOptions options)
    {
        string connectionString = DatabaseSettingsResolver.ResolveConnectionString(options);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing PostgreSQL connection string. Use --connection-string, Database:ConnectionString, or STS2_POSTGRES_CONNECTION_STRING.");
        }

        string schemaPath = Path.Combine(options.RootPath, "sts2_Annotator", "schema.sql");
        if (!File.Exists(schemaPath))
        {
            throw new FileNotFoundException($"Schema file not found at {schemaPath}");
        }

        string schemaScript = File.ReadAllText(schemaPath);

        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using NpgsqlTransaction transaction = connection.BeginTransaction();

        using (NpgsqlCommand dropCommand = new NpgsqlCommand(@"
DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN (
        SELECT tablename
        FROM pg_tables
        WHERE schemaname = 'public'
    ) LOOP
        EXECUTE format('DROP TABLE IF EXISTS public.%I CASCADE', r.tablename);
    END LOOP;
END $$;", connection, transaction))
        {
            dropCommand.ExecuteNonQuery();
        }

        using (NpgsqlCommand schemaCommand = new NpgsqlCommand(schemaScript, connection, transaction))
        {
            schemaCommand.ExecuteNonQuery();
        }

        transaction.Commit();

        Console.WriteLine("✓ Database schema reset and initialized successfully");
    }
}
