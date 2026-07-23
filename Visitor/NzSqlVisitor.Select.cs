using JustyBase.NetezzaSqlParser.Ast;

namespace JustyBase.NetezzaSqlParser.Visitor;

public partial class NzSqlVisitor
{
    public void Visit(SelectStatement stmt)
    {
        ValidateSelectStructure(stmt);
        _scope.EnterScope();
        _selectDepth++;
        _inStrictWhere = false;
        _selectOutputAliasesStack.Push(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var savedCanRef = _canReferenceSelectAliases;

        // Register CTE definitions in scope (deep-first: nested CTEs before outer)
        if (stmt.With is not null)
        {
            RegisterCteRecursive(stmt.With, registerColumns: true);

            // Validate CTE inner queries (can now reference each other in scope)
            foreach (var cte in stmt.With.Ctes)
            {
                ValidateDuplicateCteColumns(cte);
                var savedCteName = _validatingCteName;
                _validatingCteName = cte.Name;
                Visit(cte.Query);
                _validatingCteName = savedCteName;
                _validatedCtes.Add(cte.Name);
            }
        }

        // Visit FROM first to build table scope
        if (stmt.From is not null)
        {
            foreach (var tr in stmt.From)
                Visit(tr);
        }

        // Visit SELECT items
        var outputColumns = new List<string>();
        var savedInSelect = _inSelectList;
        var savedAliases = new HashSet<string>(_selectListAliasesSoFar, StringComparer.OrdinalIgnoreCase);
        _inSelectList = true;
        _selectListAliasesSoFar.Clear();

        foreach (var item in stmt.SelectList)
        {
            Visit(item.Expression);
            var name = item.Alias ?? InferSelectItemName(item.Expression);
            if (name is not null)
            {
                outputColumns.Add(name);
                _selectListAliasesSoFar.Add(name.ToUpperInvariant());
            }
        }

        var duplicateOutput = outputColumns
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateOutput is not null)
        {
            AddError(
                $"Projected column name '{duplicateOutput.Key.ToUpperInvariant()}' is repeated. Add an explicit alias so every output column name is unique.",
                "warning", "SQL049", stmt.Position);
        }

        _inSelectList = savedInSelect;
        _selectListAliasesSoFar.Clear();
        foreach (var s in savedAliases) _selectListAliasesSoFar.Add(s);

        // Update select output aliases for current SELECT
        _selectOutputAliasesStack.Pop();
        var currentAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in outputColumns) currentAliases.Add(c.ToUpperInvariant());
        _selectOutputAliasesStack.Push(currentAliases);

        // Netezza: WHERE, GROUP BY, HAVING, ORDER BY can reference SELECT aliases
        _canReferenceSelectAliases = true;

        if (stmt.Where is not null)
        {
            _inWhere = true;
            if (_selectDepth == 1)
                _inStrictWhere = true;
            Visit(stmt.Where);
            _inWhere = false;
            _inStrictWhere = false;
        }
        if (stmt.GroupBy is not null)
        {
            foreach (var g in stmt.GroupBy) Visit(g);
        }
        if (stmt.Having is not null)
        {
            _inWhere = true;
            Visit(stmt.Having);
            _inWhere = false;
        }

        _inOrderBy = true;
        if (stmt.OrderBy is not null)
        {
            foreach (var o in stmt.OrderBy) Visit(o.Expression);
        }
        _inOrderBy = false;

        if (stmt.Limit is not null) { /* no expression to visit */ }

        ValidateGroupByRules(stmt);

        // Compound selects
        if (stmt.CompoundSelects is not null)
        {
            foreach (var cs in stmt.CompoundSelects) Visit(cs);
        }

        // SQL018: Unused CTE warnings
        if (stmt.With is not null)
        {
            foreach (var cte in stmt.With.Ctes)
            {
                if (!_referencedCtes.Contains(cte.Name))
                    AddError($"CTE '{cte.Name}' is defined but never referenced", "warning", "SQL018", cte.Position);
            }
        }

        // SQL019: Unused table alias warnings
        foreach (var (alias, pos) in _tableAliases)
        {
            if (!_usedAliases.Contains(alias))
                AddError($"Table alias '{alias}' is declared but never used", "warning", "SQL019", pos);
        }
        _tableAliases.Clear();
        _usedAliases.Clear();

        _selectOutputAliasesStack.Pop();
        _canReferenceSelectAliases = savedCanRef;
        _selectDepth--;
        _scope.ExitScope();
    }

    private void LookupTableOnly(TableName tableName, SourcePosition pos)
    {
        var nameKey = tableName.Name.ToUpperInvariant();
        // Check if table exists in multi-statement scope or schema
        if (_multiStatementScope is not null && _multiStatementScope.ContainsKey(nameKey))
            return;
        if (_schema is not null)
        {
            var canValidate = tableName.Database is not null || tableName.Schema is not null
                || _schema.CanValidateUnqualifiedTableReferences();
            if (canValidate && !_schema.TableExists(tableName.Database, tableName.Schema, tableName.Name)
                && _schema.HasTables())
            {
                var fullName = FormatName(tableName.Database, tableName.Schema, tableName.Name);
                AddError($"Relation '{fullName}' does not exist",
                    "error", "SQL006", pos,
                    pos.Line, pos.Column + fullName.Length);
            }
        }
    }

    public void Visit(TableReference tr)
    {
        Visit(tr.Source);
        if (tr.Joins is not null)
        {
            foreach (var j in tr.Joins) Visit(j);
        }
    }

    public void Visit(TableSource source)
    {
        if (source.Table is not null)
        {
            var table = new TableInfo(
                source.Table.Name, source.Table.Schema, source.Table.Database,
                IsCte: false, IsTempTable: false, Alias: source.Alias);

            // Check if already in scope (CTE, temp table)
            var known = _scope.FindTable(table.Name);
            if (known is not null)
            {
                table = new TableInfo(table.Name, table.Schema, table.Database,
                    known.IsCte || table.IsCte, known.IsTempTable || table.IsTempTable,
                    table.Alias ?? known.Alias, known.Columns ?? table.Columns);
                if (known.IsCte)
                    _referencedCtes.Add(known.Name);
            }
            // Check multi-statement scope
            else if (_multiStatementScope is not null &&
                _multiStatementScope.TryGetValue(table.Name.ToUpperInvariant(), out var msTable))
            {
                table = new TableInfo(table.Name, table.Schema, table.Database,
                    msTable.IsCte, msTable.IsTempTable,
                    table.Alias, msTable.Columns ?? table.Columns);
            }

            // Get columns from schema (only for non-CTE tables)
            if (_schema is not null && !table.IsCte)
            {
                var schemaTable = _schema.GetTable(table.Database, table.Schema, table.Name);
                if (schemaTable?.Columns is not null)
                    table = new TableInfo(table.Name, table.Schema, table.Database,
                        table.IsCte, table.IsTempTable, table.Alias, schemaTable.Columns, table.Position);
            }

            // Validate table exists
            if (_schema is not null && !table.IsCte && !table.IsTempTable)
            {
                var existsInMsScope = _multiStatementScope is not null &&
                    _multiStatementScope.ContainsKey(table.Name.ToUpperInvariant());
                var canValidate = table.Database is not null || table.Schema is not null
                    || _schema.HasTables();
                if (!existsInMsScope && canValidate
                    && !_schema.TableExists(table.Database, table.Schema, table.Name)
                    && _schema.HasTables())
                {
                    var fullName = FormatName(table.Database, table.Schema, table.Name);
                    AddError($"Relation '{fullName}' does not exist",
                        "error", "SQL006", source.Position,
                        source.Position.Line, source.Position.Column + fullName.Length);
                }
                // Detect invalid single-dot form: DB.TABLE (should be DB..TABLE or SCHEMA.TABLE)
                // Only flag when the first part is a known database name, not a schema
                if (table.Database is null && table.Schema is not null)
                {
                    var databases = _schema.GetDatabases();
                    if (databases is not null &&
                        databases.Contains(table.Schema, StringComparer.OrdinalIgnoreCase))
                    {
                        var fullName = FormatName(table.Database, table.Schema, table.Name);
                        AddError($"Invalid form '{fullName}' — use database..table syntax",
                            "error", "SQL007", source.Position,
                            source.Position.Line, source.Position.Column + fullName.Length);
                    }
                }

                if (table.Database is null || table.Schema is null)
                {
                    var proposal = _schema.ProposeTableQualification(
                        table.Database, table.Schema, table.Name)?.FirstOrDefault();
                    if (proposal is not null)
                    {
                        var currentName = FormatName(table.Database, table.Schema, table.Name);
                        AddError(
                            $"Table '{currentName}' can be qualified as '{proposal.QualifiedText}'",
                            "information", "SQL048", source.Position,
                            source.Position.Line, source.Position.Column + currentName.Length,
                            proposal.QualifiedText);
                    }
                }
            }

            // Check for reserved keyword as unquoted table name
            if (source.Position.Column > 0 && IsReservedKeyword(table.Name))
            {
                AddError($"Unquoted reserved keyword '{table.Name.ToUpperInvariant()}' cannot be used as table name. Use \"{table.Name.ToUpperInvariant()}\"",
                    "error", "SQL015", source.Position);
            }

            // Don't re-add CTE tables to the scope — they're already in Ctes
            var aliasKey = (table.Alias ?? table.Name).ToUpperInvariant();
            var existingTable = _scope.FindTable(aliasKey);
            // Also check by table name (CTEs are stored under their own name, not the alias)
            if (existingTable is null && table.Alias is not null)
                existingTable = _scope.FindTable(table.Name.ToUpperInvariant());
            if (existingTable is not null && existingTable.IsCte)
            {
                var key = aliasKey;
                // SQL019: only track explicitly declared aliases (not implicit table-name aliases)
                if (table.Alias is not null)
                    _tableAliases[key] = source.AliasPosition ?? source.Position;
                _scope.AddTable(table);
            }
            else
            {
                var key = aliasKey;
                // SQL019: only track explicitly declared aliases (not implicit table-name aliases)
                if (table.Alias is not null)
                    _tableAliases[key] = source.AliasPosition ?? source.Position;
                var duplicate = _scope.AddTable(table);
                if (duplicate is not null)
                {
                    AddError($"Table name \"{key}\" specified more than once",
                        "error", "SQL011", source.Position);
                }
            }
        }

        if (source.FunctionSource && source.Alias is not null)
        {
            var funcInfo = new TableInfo(source.Alias, IsCte: false, IsTempTable: false);
            _scope.AddTable(funcInfo);
        }

        if (source.Subquery is not null)
        {
            if (source.Alias is null)
                AddError("Subquery in FROM/JOIN must have an alias", "error", "SQL020", source.Position);
            Visit(source.Subquery);
            if (source.Alias is not null)
            {
                var subCols = InferColumnsFromSelect(source.Subquery);
                var subInfo = new TableInfo(source.Alias, IsCte: false, IsTempTable: false,
                    Columns: subCols.Count > 0 ? subCols : null);
                _scope.AddTable(subInfo);
            }
        }
    }

    private void RegisterCteRecursive(WithClause with, bool registerColumns)
    {
        // Step 1: register ALL CTE names (including nested) with empty columns
        // so that forward references work (e.g., CTE1 → CTE2 where CTE2 is nested)
        foreach (var cte in with.Ctes)
        {
            // Skip if already registered (e.g., from deeper recursion that hit
            // this WITH clause via Visit(cte.Query))
            if (_scope.FindTable(cte.Name) is not null)
                continue;

            var emptyInfo = new CteInfo(cte.Name, with.Recursive, null);
            _scope.AddCte(emptyInfo);

            if (cte.Query.With is not null)
                RegisterCteRecursive(cte.Query.With, registerColumns: false);
        }

        // Step 2: resolve columns for each CTE (depth-first: nested first)
        ResolveCteColumns(with);

        // Step 3: second pass for forward references within same WITH level
        // (e.g., CTE1 references CTE2 defined later in the same WITH clause)
        bool anyResolved;
        do
        {
            anyResolved = false;
            foreach (var cte in with.Ctes)
            {
                var existing = _scope.FindTable(cte.Name);
                if (existing?.Columns is not null && existing.Columns.Count > 0)
                    continue; // already has columns

                if (cte.Query.With is not null)
                    RegisterCteRecursive(cte.Query.With, registerColumns: true);

                var cols = BuildCteColumns(cte);
                if (cols.Count > 0)
                {
                    var cteInfo = new CteInfo(cte.Name, with.Recursive, cols);
                    _scope.AddCte(cteInfo);
                    anyResolved = true;
                }
            }
        } while (anyResolved);
    }

    private List<ColumnInfo> BuildCteColumns(CteDefinition cte)
    {
        var cols = new List<ColumnInfo>();
        if (cte.Columns is { Count: > 0 })
        {
            cols.AddRange(cte.Columns.Select(c => new ColumnInfo(c)));
        }
        else
        {
            foreach (var item in cte.Query.SelectList)
            {
                var name = item.Alias ?? InferSelectItemName(item.Expression);
                if (name is not null)
                {
                    var dataType = item.Alias is null
                        ? ResolveSelectItemDataType(item.Expression)
                        : null;
                    cols.Add(new ColumnInfo(name, DataType: dataType));
                }
                else if (item.Expression is StarExpression star)
                {
                    var expanded = ExpandStarColumns(cte.Query.From, star.Qualifier);
                    cols.AddRange(expanded.Select(c => new ColumnInfo(c)));
                }
            }
        }
        return cols;
    }

    private void ValidateDuplicateCteColumns(CteDefinition cte)
    {
        var columns = BuildCteColumns(cte);
        var duplicate = columns
            .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            AddError(
                $"Duplicate column name '{duplicate.Key.ToUpperInvariant()}' in CTE '{cte.Name}'",
                "error", "SQL023", cte.Position);
        }
    }

    private string? ResolveSelectItemDataType(Expression expr) => expr switch
    {
        ColumnReference cr => ResolveColumnDataType(cr),
        CastExpression c => c.TargetType.Name,
        CastFunctionExpression cf => cf.TargetType.Name,
        Literal l => l.Kind switch
        {
            LiteralKind.Number => "NUMERIC",
            LiteralKind.String => "VARCHAR",
            _ => null
        },
        _ => null
    };

    private void ResolveCteColumns(WithClause with)
    {
        foreach (var cte in with.Ctes)
        {
            if (cte.Query.With is not null)
                RegisterCteRecursive(cte.Query.With, registerColumns: true);

            var existing = _scope.FindTable(cte.Name);
            if (existing?.Columns is not null)
                continue;

            var cols = BuildCteColumns(cte);
            var cteInfo = new CteInfo(cte.Name, with.Recursive,
                cols.Count > 0 ? cols : null);
            _scope.AddCte(cteInfo);
        }
    }

    private List<string> ExpandStarColumns(IReadOnlyList<TableReference>? from, string? starQualifier)
    {
        var result = new List<string>();
        if (from is null) return result;

        // Collect unique qualifier → table name mappings for alias resolution
        var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tr in from)
        {
            CollectAliases(tr, aliasMap);
        }

        foreach (var tr in from)
        {
            ExpandFromTableRef(tr, result, starQualifier, aliasMap);
        }

        return result;
    }

    private void CollectAliases(TableReference tr, Dictionary<string, string> aliasMap)
    {
        if (tr.Source.Alias is not null && tr.Source.Table is not null)
        {
            aliasMap[tr.Source.Alias] = tr.Source.Table.Name;
        }
        if (tr.Joins is not null)
        {
            foreach (var join in tr.Joins)
                CollectAliasesFromJoin(join, aliasMap);
        }
    }

    private void CollectAliasesFromJoin(JoinClause join, Dictionary<string, string> aliasMap)
    {
        if (join.Source.Alias is not null && join.Source.Table is not null)
        {
            aliasMap[join.Source.Alias] = join.Source.Table.Name;
        }
        if (join.Source.Table is not null && join.Source.Alias is null)
        {
            // Bare table reference (no alias) — register table name itself as qualifier
            aliasMap[join.Source.Table.Name] = join.Source.Table.Name;
        }
    }

    private void ExpandFromTableRef(TableReference tr, List<string> result, string? qualifier,
        Dictionary<string, string> aliasMap)
    {
        // If qualifier specified (e.g., t.*), resolve alias first
        string? targetName = qualifier;
        if (qualifier is not null && aliasMap.TryGetValue(qualifier, out var resolved))
            targetName = resolved;

        ExpandSource(tr.Source, result, targetName, qualifier is null);

        if (tr.Joins is not null)
        {
            foreach (var join in tr.Joins)
                ExpandFromJoin(join, result, qualifier, aliasMap);
        }
    }

    private void ExpandFromJoin(JoinClause join, List<string> result, string? qualifier,
        Dictionary<string, string> aliasMap)
    {
        string? targetName = qualifier;
        if (qualifier is not null && join.Source.Alias is not null
            && string.Equals(join.Source.Alias, qualifier, StringComparison.OrdinalIgnoreCase))
        {
            targetName = join.Source.Table?.Name;
        }
        else if (qualifier is not null && join.Source.Table is not null
            && string.Equals(join.Source.Table.Name, qualifier, StringComparison.OrdinalIgnoreCase))
        {
            targetName = join.Source.Table.Name;
        }

        ExpandSource(join.Source, result, targetName, qualifier is null);
    }

    private void ExpandSource(TableSource source, List<string> result, string? targetName, bool noQualifier)
    {
        if (source.Table is null) return;
        var tableName = source.Table.Name;

        // If a specific qualifier is requested, only match that table
        if (targetName is not null
            && !string.Equals(tableName, targetName, StringComparison.OrdinalIgnoreCase)
            && (source.Alias is null
                || !string.Equals(source.Alias, targetName, StringComparison.OrdinalIgnoreCase)))
            return;

        if (noQualifier && source.Alias is not null)
        {
            // For unqualified *, tables with aliases are still included
        }

        // Try schema provider first
        if (_schema is not null)
        {
            var info = _schema.GetTable(source.Table.Database, source.Table.Schema, tableName);
            if (info?.Columns is not null)
            {
                foreach (var col in info.Columns)
                    result.Add(col.Name);
                return;
            }
        }

        // Fallback: check if this table is a CTE registered in scope
        var scopeTable = _scope.FindTable(tableName);
        if (scopeTable?.Columns is not null)
        {
            foreach (var col in scopeTable.Columns)
                result.Add(col.Name);
        }
    }

    public void Visit(JoinClause join)
    {
        Visit(join.Source);
        if (join.Type == JoinType.Cross)
        {
            if (join.OnCondition is not null)
                AddError("CROSS JOIN should not have ON clause", "warning", "SQL002", join.Position);
            else if (join.UsingColumns is { Count: > 0 })
                AddError("CROSS JOIN should not have USING clause", "warning", "SQL002", join.Position);
        }
        else if (!join.Natural && join.OnCondition is null &&
            (join.UsingColumns is null || join.UsingColumns.Count == 0))
        {
            AddError("JOIN requires ON or USING clause", "error", "SQL027", join.Position);
        }
        if (join.OnCondition is not null)
        {
            _implicitOnContext = true;
            Visit(join.OnCondition);
            _implicitOnContext = false;
        }
    }
}
