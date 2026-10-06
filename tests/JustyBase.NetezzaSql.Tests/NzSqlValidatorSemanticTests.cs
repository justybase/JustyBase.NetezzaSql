using JustyBase.NetezzaSqlParser.Ast;
using JustyBase.NetezzaSqlParser.Lexer;
using JustyBase.NetezzaSqlParser.Parser;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorSemanticTests
{
    private readonly ISchemaProvider _schema;


    public static TheoryData<string, string> NetezzaExtensionFunctionData => new()
    {
        { "BTRIM", "SELECT BTRIM('  hi  ');" },
        { "INSTR", "SELECT INSTR('Hello World', 'o');" },
        { "STRPOS", "SELECT STRPOS('Hello World', 'o');" },
        { "UNICHR", "SELECT UNICHR(65);" },
        { "UNICODE", "SELECT UNICODE('A');" },
        { "UNICODES", "SELECT UNICODES('AZ');" },
        { "OVERLAPS", "SELECT OVERLAPS(1, 2, 3, 4);" },
        { "DURATION_ADD", "SELECT DURATION_ADD(1, 2);" },
        { "DURATION_SUBTRACT", "SELECT DURATION_SUBTRACT(2, 1);" },
        { "TIMEOFDAY", "SELECT TIMEOFDAY();" },
        { "TIMEZONE", "SELECT TIMEZONE(NOW(), 'UTC', 'UTC');" },
        { "HEX_TO_BINARY", "SELECT HEX_TO_BINARY('DEADBEEF');" },
        { "HEX_TO_GEOMETRY", "SELECT HEX_TO_GEOMETRY('00');" },
        { "INT_TO_STRING", "SELECT INT_TO_STRING(42, 16);" },
        { "STRING_TO_INT", "SELECT STRING_TO_INT('2A', 16);" },
        { "ISFALSE", "SELECT ISFALSE(1 = 0);" },
        { "ISNOTFALSE", "SELECT ISNOTFALSE(1 = 1);" },
        { "ISTRUE", "SELECT ISTRUE(1 = 1);" },
        { "ISNOTTRUE", "SELECT ISNOTTRUE(1 = 0);" },
        { "VERSION", "SELECT VERSION();" },
        { "GET_VIEWDEF", "SELECT GET_VIEWDEF('EMP_VIEW');" },
        { "SETSEED", "SELECT SETSEED(0.5);" },
        { "DCEIL", "SELECT DCEIL(42.8);" },
        { "DFLOOR", "SELECT DFLOOR(42.8);" },
        { "FPOW", "SELECT FPOW(9.0, 3.0);" },
        { "NUMERIC_SQRT", "SELECT NUMERIC_SQRT(2);" },
        { "POW", "SELECT POW(9.0, 3.0);" },
        { "INT1AND", "SELECT INT1AND(3, 6);" },
        { "INT1OR", "SELECT INT1OR(3, 6);" },
        { "INT1XOR", "SELECT INT1XOR(3, 6);" },
        { "INT1NOT", "SELECT INT1NOT(3);" },
        { "INT1SHL", "SELECT INT1SHL(3, 1, 6);" },
        { "INT1SHR", "SELECT INT1SHR(3, 1, 6);" },
        { "INT2AND", "SELECT INT2AND(3, 6);" },
        { "INT2OR", "SELECT INT2OR(3, 6);" },
        { "INT2XOR", "SELECT INT2XOR(3, 6);" },
        { "INT2NOT", "SELECT INT2NOT(3);" },
        { "INT2SHL", "SELECT INT2SHL(3, 1, 6);" },
        { "INT2SHR", "SELECT INT2SHR(3, 1, 6);" },
        { "INT4AND", "SELECT INT4AND(3, 6);" },
        { "INT4OR", "SELECT INT4OR(3, 6);" },
        { "INT4XOR", "SELECT INT4XOR(3, 6);" },
        { "INT4NOT", "SELECT INT4NOT(3);" },
        { "INT4SHL", "SELECT INT4SHL(3, 1, 6);" },
        { "INT4SHR", "SELECT INT4SHR(3, 1, 6);" },
        { "INT8AND", "SELECT INT8AND(3, 6);" },
        { "INT8OR", "SELECT INT8OR(3, 6);" },
        { "INT8XOR", "SELECT INT8XOR(3, 6);" },
        { "INT8NOT", "SELECT INT8NOT(3);" },
        { "INT8SHL", "SELECT INT8SHL(3, 1, 6);" },
        { "INT8SHR", "SELECT INT8SHR(3, 1, 6);" },
    };
}
