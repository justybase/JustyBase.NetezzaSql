using System.Text.Json;
using JustyBase.NetezzaDriver;
using Xunit;

namespace JustyBase.NetezzaSql.IntegrationTests;

public sealed class AuthoringLanguageTruthTests
{
    [OptInLiveConformanceFact]
    public void P0AuthoringLanguageTruth_ReadOnlyQueries()
    {
        string Env(string name) => Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Missing {name}");
        using var connection = new NzConnection(Env("NZ_DEV_USER"), Env("NZ_DEV_PASSWORD"),
            Env("NZ_DEV_HOST"), Env("NZ_DEV_DATABASE"), int.Parse(Env("NZ_DEV_PORT")));
        connection.Open();
        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT VERSION()";
        Console.WriteLine("AUTHORING_SERVER_VERSION=" + versionCommand.ExecuteScalar());
        foreach (var (id, sql, expected) in new[]
        {
            ("netezza.diagnostics.nzl008-empty-in-list.024", "SELECT 1 WHERE 1 IN ()", false),
            ("netezza.diagnostics.nzs002-fetch-first-rejected.025", "SELECT 1 FETCH FIRST 5 ROWS ONLY", false),
            ("netezza.semantic.statement-model-multiple-ctes.029", "WITH a AS (SELECT 1 AS X), b AS (SELECT 2 AS Y) SELECT * FROM a JOIN b ON a.X = b.Y", true),
            ("netezza.semantic.quality-cte-shadowing", "WITH recent AS (SELECT 1 AS id) SELECT * FROM (WITH recent AS (SELECT 2 AS id) SELECT * FROM recent) d", true)
        })
        {
            bool valid;
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                using var reader = command.ExecuteReader();
                valid = true;
            }
            catch (System.Data.Common.DbException) { valid = false; }
            Console.WriteLine("AUTHORING_ORACLE=" + JsonSerializer.Serialize(new { id, sql, expected, actual = valid }));
            Assert.Equal(expected, valid);
        }
    }
}
