using System.Text;
using JustyBase.NetezzaDdl.Models;

namespace JustyBase.NetezzaDdl;

/// <summary>
/// Renders a complete, ordered deployment script from catalog-derived objects.
/// </summary>
public sealed class NetezzaBatchDdlBuilder
{
    private readonly NetezzaDdlTextBuilder _singleObjectBuilder;

    public NetezzaBatchDdlBuilder(NetezzaDdlTextBuilder? singleObjectBuilder = null)
        => _singleObjectBuilder = singleObjectBuilder ?? new NetezzaDdlTextBuilder();

    public string Build(NetezzaBatchDdlInput input)
        => BuildDetailed(input).Sql;

    public NetezzaBatchDdlResult BuildDetailed(NetezzaBatchDdlInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sql = new StringBuilder();
        var skipped = new List<string>();

        foreach (var table in input.Tables ?? [])
        {
            if (table.Columns.Count == 0)
            {
                skipped.Add(QualifiedName(table.Database, table.Schema, table.TableName));
                continue;
            }

            AppendHeader(sql, "TABLE", table.Database, table.Schema, table.TableName);
            if (input.RecreateTables)
                _singleObjectBuilder.AppendRecreateTable(sql, table);
            else
                _singleObjectBuilder.AppendCreateTable(sql, table);
        }

        foreach (var external in input.ExternalTables ?? [])
        {
            if (external.Columns.Count == 0)
            {
                skipped.Add(QualifiedName(external.Database, external.Schema, external.TableName));
                continue;
            }

            AppendHeader(sql, "EXTERNAL TABLE", external.Database, external.Schema, external.TableName);
            _singleObjectBuilder.AppendCreateExternal(sql, external);
        }

        foreach (var view in input.Views ?? [])
        {
            if (string.IsNullOrWhiteSpace(view.ViewDefinition))
            {
                skipped.Add(QualifiedName(view.Database, view.Schema, view.ViewName));
                continue;
            }

            AppendHeader(sql, "VIEW", view.Database, view.Schema, view.ViewName);
            _singleObjectBuilder.AppendCreateView(sql, view);
        }

        foreach (var procedure in input.Procedures ?? [])
        {
            if (string.IsNullOrWhiteSpace(procedure.ProcedureSource))
            {
                skipped.Add(QualifiedName(procedure.Database, procedure.Schema, procedure.ProcedureName));
                continue;
            }

            AppendHeader(sql, "PROCEDURE", procedure.Database, procedure.Schema, procedure.ProcedureName);
            _singleObjectBuilder.AppendCreateProcedure(sql, procedure);
        }

        foreach (var synonym in input.Synonyms ?? [])
        {
            if (string.IsNullOrWhiteSpace(synonym.ReferencedObject))
            {
                skipped.Add(QualifiedName(synonym.Database, synonym.Schema, synonym.SynonymName));
                continue;
            }

            AppendHeader(sql, "SYNONYM", synonym.Database, synonym.Schema, synonym.SynonymName);
            _singleObjectBuilder.AppendCreateSynonym(sql, synonym);
        }

        if (skipped.Count > 0)
        {
            sql.AppendLine("-- Objects skipped because required catalog metadata was missing:");
            foreach (var skippedObject in skipped)
                sql.AppendLine($"-- {skippedObject}");
        }

        return new NetezzaBatchDdlResult(sql.ToString(), skipped);
    }

    private static void AppendHeader(
        StringBuilder sql,
        string objectType,
        string database,
        string schema,
        string name)
    {
        if (sql.Length > 0)
            sql.AppendLine();

        sql.AppendLine($"-- {objectType} {QualifiedName(database, schema, name)}");
    }

    private static string QualifiedName(string database, string schema, string name)
        => string.Join('.', new[] { database, schema, name }.Where(part => !string.IsNullOrWhiteSpace(part)));
}
