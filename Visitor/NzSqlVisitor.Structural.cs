using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Visitor;

public partial class NzSqlVisitor
{
    private void ValidateDeleteStructure(DeleteStatement stmt)
    {
        if (stmt.Where is null)
        {
            AddError("DELETE without WHERE clause will delete all rows",
                "error", "SQL043", stmt.Position);
        }
    }

    private void ValidateUpdateStructure(UpdateStatement stmt)
    {
        if (stmt.Alias is not null)
        {
            AddError("UPDATE table AS alias is not supported in Netezza — use UPDATE table alias without AS",
                "error", "SQL046", stmt.Position);
        }

        if (stmt.Where is null)
        {
            AddError("UPDATE without WHERE clause will update all rows",
                "error", "SQL044", stmt.Position);
        }
    }

    private void ValidateSelectStructure(SelectStatement stmt)
    {
        if (stmt.Where is not null && (stmt.From is null || stmt.From.Count == 0))
        {
            AddError("WHERE clause without FROM is not valid",
                "error", "SQL042", stmt.Where.Position);
        }
    }

    private void ValidateCreateTableStructure(CreateTableStatement stmt)
    {
        if (stmt.AsSelect is not null && stmt.Distribute is null && !stmt.Temporary)
        {
            AddError("CREATE TABLE AS SELECT should include DISTRIBUTE ON clause",
                "warning", "SQL045", stmt.Position);
        }
    }

    private void ValidateCaseStructure(CaseExpression ce)
    {
        if (ce.WhenClauses.Count == 0)
        {
            AddError("CASE expression must have at least one WHEN clause",
                "error", _inProcedureContext ? "SQL041" : "PAR005", ce.Position);
        }
    }
}
