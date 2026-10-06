using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Dialects;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzCompletionEngineTests
{
    private readonly InMemorySchemaProvider _schema = new();

    private readonly NzCompletionEngine _engine;


    public NzCompletionEngineTests()
    {
        _schema.AddTable(new TableInfo("employees", Columns: new[]
        {
            new ColumnInfo("id"), new ColumnInfo("name"), new ColumnInfo("salary"), new ColumnInfo("dept_id")
        }));
        _schema.AddTable(new TableInfo("departments", Columns: new[]
        {
            new ColumnInfo("id"), new ColumnInfo("name"), new ColumnInfo("location")
        }));
        _engine = new NzCompletionEngine(_schema);
    }
}
