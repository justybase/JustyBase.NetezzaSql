using JustyBase.Ai.Services;

namespace JustyBase.Ai.Tests;

public sealed class NetezzaReferenceProviderTests
{
    [Fact]
    public void GetNetezzaReference_Optimization_ShouldReturnOptimizationSection()
    {
        var provider = new NetezzaReferenceProvider();
        var result = provider.GetNetezzaReference("optimization");

        Assert.Contains("NETEZZA OPTIMIZATION RULES", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NZPLSQL STORED PROCEDURE REFERENCE", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetNetezzaReference_Nzplsql_ShouldReturnProcedureSection()
    {
        var provider = new NetezzaReferenceProvider();
        var result = provider.GetNetezzaReference("nzplsql");

        Assert.Contains("NZPLSQL STORED PROCEDURE REFERENCE", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN_PROC", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetNetezzaReference_All_ShouldReturnCombinedSections()
    {
        var provider = new NetezzaReferenceProvider();
        var result = provider.GetNetezzaReference();

        Assert.Contains("NETEZZA OPTIMIZATION RULES", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NZPLSQL STORED PROCEDURE REFERENCE", result, StringComparison.OrdinalIgnoreCase);
    }
}
