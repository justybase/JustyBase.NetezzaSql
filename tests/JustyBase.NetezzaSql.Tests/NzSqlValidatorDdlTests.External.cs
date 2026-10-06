using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorDdlTests
{
    [Fact]
    public void ExplainVerboseDistributionPlantext()
    {
        ExpectValid("EXPLAIN VERBOSE DISTRIBUTION PLANTEXT SELECT 1;", _schema);
    }


    [Fact]
    public void CommentOn_Column()
    {
        ExpectValid(
            "COMMENT ON COLUMN TESTDB.PUBLIC.EMPLOYEES.SALARY IS 'Annual salary';",
            _schema);
    }


    [Fact]
    public void CommentOn_Procedure()
    {
        ExpectValid("COMMENT ON PROCEDURE MY_PROC IS 'Helper procedure';", _schema);
    }




    [Fact]
    public void ExplainDistributionSelect()
    {
        ExpectValid("EXPLAIN DISTRIBUTION SELECT * FROM my_table", _schema);
    }
}
