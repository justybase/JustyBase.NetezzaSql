using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorSemanticTests
{
    [Fact]
    public void Validate_SemanticAdditional_InvalidDataTypeInCreateTable()
    {
        SqlTestHelpers.ExpectErrorCode(
            "CREATE TABLE TESTDB..BAD_TYPE (ID FAKE_TYPE);",
            "SQL013",
            _schema);
    }


    [Fact]
    public void Validate_SemanticAdditional_ExcessTypeParameters()
    {
        SqlTestHelpers.ExpectErrorCode(
            "CREATE TABLE TESTDB..BAD_PARAMS (ID INT4(10,2));",
            "SQL014",
            _schema);
    }
}
