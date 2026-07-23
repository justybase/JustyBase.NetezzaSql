using JustyBase.NetezzaSqlParser.Authoring;
using JustyBase.NetezzaSqlParser.Lexer;
using Superpower.Model;

namespace JustyBase.NetezzaSqlParser.Completion;

public enum AlterTablePhase
{
    TopLevel,
    Add,
    AddColumn,
    AddColumnType,
    Drop,
    DropColumn,
    AlterColumn,
    Rename,
    RenameColumn,
    RenameColumnTarget,
    ModifyColumn,
    OwnerTo,
    OrganizeOn,
    DistributeOn,
}

/// <summary>
/// Netezza ALTER TABLE completion phases and keyword lists.
/// Port of alterTableCompletion.ts from the reference project.
/// </summary>
public static class AlterTableCompletion
{
    public static readonly string[] TopLevelActions =
    [
        "ADD COLUMN", "ADD", "ALTER COLUMN", "DROP COLUMN", "DROP CONSTRAINT",
        "MODIFY COLUMN", "OWNER TO", "RENAME COLUMN", "RENAME TO",
        "SET PRIVILEGES TO", "ORGANIZE ON", "DISTRIBUTE ON",
    ];

    public static readonly string[] DataTypes =
    [
        "BOOLEAN", "BYTEINT", "SMALLINT", "INTEGER", "BIGINT", "REAL", "DOUBLE",
        "NUMERIC", "DECIMAL", "CHAR", "VARCHAR", "NCHAR", "NVARCHAR",
        "DATE", "TIME", "TIMESTAMP", "TIMESTAMPTZ", "INTERVAL", "VARBYTE",
    ];

    public static AlterTablePhase AnalyzePhase(Token<NzToken>[] tokens)
    {
        var phase = AlterTablePhase.TopLevel;
        var afterAlterTable = false;
        var sawTableName = false;

        for (int i = 0; i < tokens.Length; i++)
        {
            var kind = tokens[i].Kind;
            if (kind == NzToken.Alter) { afterAlterTable = false; sawTableName = false; continue; }
            if (kind == NzToken.Table)
            {
                afterAlterTable = true;
                var (_, consumed) = SkipTablePath(tokens, i + 1);
                if (consumed > 0)
                {
                    sawTableName = true;
                    i += consumed;
                }
                continue;
            }
            if (!afterAlterTable || !sawTableName) continue;

            if (kind == NzToken.Add) phase = AlterTablePhase.Add;
            else if (kind == NzToken.Drop) phase = AlterTablePhase.Drop;
            else if (kind == NzToken.Alter) phase = AlterTablePhase.AlterColumn;
            else if (kind == NzToken.Owner) phase = AlterTablePhase.OwnerTo;
            else if (kind == NzToken.Organize) phase = AlterTablePhase.OrganizeOn;
            else if (kind == NzToken.Distribute) phase = AlterTablePhase.DistributeOn;
            else if (kind == NzToken.Column)
            {
                phase = phase switch
                {
                    AlterTablePhase.Add => AlterTablePhase.AddColumn,
                    AlterTablePhase.Drop => AlterTablePhase.DropColumn,
                    AlterTablePhase.AlterColumn => AlterTablePhase.AlterColumn,
                    AlterTablePhase.Rename => AlterTablePhase.RenameColumn,
                    AlterTablePhase.ModifyColumn => AlterTablePhase.ModifyColumn,
                    _ => phase
                };
            }
            else if (kind is NzToken.Identifier or NzToken.QuotedIdentifier)
            {
                var text = tokens[i].ToStringValue().ToUpperInvariant();
                if (text == "RENAME") phase = AlterTablePhase.Rename;
                else if (text == "MODIFY") phase = AlterTablePhase.ModifyColumn;
                else if (phase == AlterTablePhase.Drop && !IsAlterActionKeyword(text))
                    phase = AlterTablePhase.DropColumn;
                else if (phase == AlterTablePhase.Rename && text != "TO")
                    phase = AlterTablePhase.RenameColumn;
                else if (phase == AlterTablePhase.AddColumn)
                    phase = AlterTablePhase.AddColumnType;
            }
            else if (kind == NzToken.To && phase == AlterTablePhase.RenameColumn)
                phase = AlterTablePhase.RenameColumnTarget;
        }

        // DROP shorthand: cursor after DROP with no COLUMN keyword yet
        if (phase == AlterTablePhase.Drop && tokens.Length > 0 && tokens[^1].Kind == NzToken.Drop)
            phase = AlterTablePhase.DropColumn;

        return phase;
    }

    private static bool IsAlterActionKeyword(string upperText) =>
        upperText is "COLUMN" or "CONSTRAINT" or "CASCADE" or "RESTRICT";

    private static (string Name, int Consumed) SkipTablePath(Token<NzToken>[] tokens, int start)
    {
        int i = start;
        int begin = i;
        string? lastName = null;

        while (i < tokens.Length)
        {
            if (tokens[i].Kind is NzToken.Identifier or NzToken.QuotedIdentifier)
            {
                lastName = tokens[i].ToStringValue();
                i++;
                continue;
            }
            if (tokens[i].Kind == NzToken.Dot)
            {
                i++;
                continue;
            }
            break;
        }

        return lastName is null ? (string.Empty, 0) : (lastName, i - begin);
    }

    public static IReadOnlyList<string> GetKeywordsForPhase(AlterTablePhase phase) => phase switch
    {
        AlterTablePhase.TopLevel => TopLevelActions,
        AlterTablePhase.Add => ["COLUMN", "CONSTRAINT"],
        AlterTablePhase.Drop => ["COLUMN", "CONSTRAINT"],
        AlterTablePhase.Rename => ["COLUMN", "TO"],
        AlterTablePhase.AddColumnType => NetezzaSqlCatalog.DataTypeNames.ToArray(),
        AlterTablePhase.OrganizeOn => ["NONE"],
        AlterTablePhase.DistributeOn => ["ON", "HASH", "RANDOM"],
        _ => Array.Empty<string>()
    };

    public static bool PhaseNeedsTableColumns(AlterTablePhase phase) =>
        phase is AlterTablePhase.DropColumn or AlterTablePhase.AlterColumn
            or AlterTablePhase.RenameColumn or AlterTablePhase.ModifyColumn
            or AlterTablePhase.OrganizeOn or AlterTablePhase.DistributeOn;
}
