using JustyBase.NetezzaSqlParser.Visitor;
using static JustyBase.Tests.NetezzaSqlParser.SqlTestHelpers;

namespace JustyBase.Tests.NetezzaSqlParser;

public sealed partial class NzSqlValidatorDdlTests
{
    [Fact]
    public void CommentOn_View()
    {
        ExpectValid("COMMENT ON VIEW V_EMP IS 'Employee view';", _schema);
    }
}
