namespace JustyBase.NetezzaCatalogSql;

/// <summary>Helper for normalizing Netezza procedure return types.</summary>
public static class NetezzaProcTypes
{
    /// <summary>Appends <c>(ANY)</c> to unparameterised character types returned by the catalog.</summary>
    public static string FixProcedureReturnType(string procReturns)
    {
        return procReturns switch
        {
            "CHARACTER VARYING" => "CHARACTER VARYING(ANY)",
            "NATIONAL CHARACTER VARYING" => "NATIONAL CHARACTER VARYING(ANY)",
            "NATIONAL CHARACTER" => "NATIONAL CHARACTER(ANY)",
            "CHARACTER" => "CHARACTER(ANY)",
            _ => procReturns
        };
    }
}
