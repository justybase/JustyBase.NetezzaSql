using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed class NzBaseParserArchitectureTests
{
    [Fact]
    public void Validate_Accepts_DbDotDotTable()
    {
        ExpectValid("SELECT * FROM DB1..TABLE1;");
    }

    [Fact]
    public void Validate_Accepts_TableWithFinal()
    {
        ExpectValid("SELECT F.* FROM TABLE WITH FINAL (DB1.SCH1.FLUID_FN()) F;");
    }
}
