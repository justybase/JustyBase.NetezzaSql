using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorDdlTests
{
    [Fact]
    public void ExplainRejectsMultipleOptions()
    {
        ExpectSyntaxError("EXPLAIN VERBOSE DISTRIBUTION PLANTEXT SELECT 1;", _schema);
    }

    [Theory]
    [InlineData("EXPLAIN SELECT 1;")]
    [InlineData("EXPLAIN VERBOSE SELECT 1;")]
    [InlineData("EXPLAIN DISTRIBUTION SELECT 1;")]
    [InlineData("EXPLAIN PLANTEXT SELECT 1;")]
    [InlineData("EXPLAIN PLANGRAPH SELECT 1;")]
    public void ExplainAcceptsBaseAndSingleOptions(string sql)
    {
        ExpectValid(sql, _schema);
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
