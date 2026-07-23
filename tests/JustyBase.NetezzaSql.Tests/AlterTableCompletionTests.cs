using JustyBase.NetezzaSqlParser.Completion;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class AlterTableCompletionTests
{
    private readonly NzCompletionEngine _engine;
    private readonly InMemorySchemaProvider _schema;

    public AlterTableCompletionTests()
    {
        _schema = (InMemorySchemaProvider)SqlTestHelpers.CreateStandardMockSchema();
        _engine = new NzCompletionEngine(_schema);
    }

    [Fact]
    public void AlterTable_DropShorthand_SuggestsColumns()
    {
        var sql = "ALTER TABLE EMPLOYEES DROP ";
        var items = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(items, i => i.Label.Equals("STATUS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AlterTable_QualifiedTable_DropShorthand_SuggestsColumns()
    {
        var sql = "ALTER TABLE TESTDB..EMPLOYEES DROP ";
        var items = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(items, i => i.Label.Equals("EMPLOYEE_ID", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AlterTable_DropColumnKeyword_StillSuggestsColumns()
    {
        var sql = "ALTER TABLE EMPLOYEES DROP COLUMN ";
        var items = _engine.GetCompletions(sql, sql.Length);
        Assert.Contains(items, i => i.Label.Equals("SALARY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AlterTable_DropColumnShorthand_ParsesValid()
    {
        SqlTestHelpers.ExpectValid("ALTER TABLE TESTDB..EMPLOYEES DROP STATUS;", _schema);
    }
}
