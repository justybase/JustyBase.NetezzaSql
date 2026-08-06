# Shared Core Status

This document tracks which `JustyBase.Core` / `JustyBase.ImportExport` surfaces are **production** vs **scaffold**.

| Area | Status | Notes |
| --- | --- | --- |
| Risk (`SqlRiskAnalysisService`, `SqlRiskGate`) | **Production** | Consumed by Avalonia RUN/lint and Legacy Application wrapper |
| Import SQL + USING builder | **Production** | Always emits `REMOTESOURCE 'dotnet'` (client-side DATAOBJECT/pipe) |
| Named-pipe import executor | **Production** | Avalonia `NetezzaImportHelper` + Legacy `ImportExportTasks` (`LinesPipeServer` / `FileStreamPipeServer` / `DBReaderStreamPipeServer`) adapt to `NetezzaPipeImportExecutor` |
| Script dialect | **Production (partial)** | Avalonia `AvaloniaScriptDialect`. Legacy `SpecialCommandService` keeps host FS I/O. Pure `__Let` / sleep normalize / `__SessionVar__` expression eval (+ `ISessionVarEvaluator` for `SQL_RESULT`) live in `LegacySqlDirectiveProcessor` |
| CSV export (`CsvExportWriter`) | **Production** | Legacy `TabularTextExporter` + Avalonia uncompressed CSV path |
| Parquet + gzip/zip export | **Production** | `ParquetExportWriter` + `CompressedExportStreams` in ImportExport; Avalonia façade uses them. Excel / LZ4 / Brotli / Zstd remain host-backed |
| Excel export | **Host-backed** | Different Xlsx/Xlsb writers per host |
| Execution runner (`SqlExecutionRunner`) | **Scaffold (deferred)** | Contracts ready; hosts still use local runners |
| Schema cache | **Production (Avalonia + Legacy)** | `NetezzaSchemaLoader` + `NetezzaSchemaCache` in `JustyBase.Netezza` are the shared catalog-loading SoT. Avalonia `NetezzaBase` plugin feeds its host stores from loader snapshots; Legacy `InitializeConnectionSchemaData` builds `NetezzaTableInfo`/column intervals/lookup/owners from loader snapshots, merges DISTSEQNO/ORGSEQNO from `GetLegacyDistributionColumnsSql`, and optionally feeds `NetezzaSchemaCache`. The retired `NetezzaCatalogSql.Legacy.cs` is deleted; Legacy `DownloadSchemaNetezza` keeps only the databases, descriptions and keys stages (`GetLegacyKeysSql`) |
| Schema context menu SQL templates | **Production (partial)** | Core `SchemaContextMenuCatalog` is shared SQL SoT (`Ids.*`, `Format`). Avalonia/Legacy keep richer UI trees and host-only actions |
| History / snippets / session vars persistence | **Scaffold (deferred)** | In-memory Core API only; hosts keep file/DB persistence |
| Grid `CellStatsCalculator` | **Production (Avalonia + Legacy)** | Typed numeric **selection** stats shown in the window bottom bar (80 ms debounce). Avalonia `ResultGridStatsService` and Legacy `CustomDataGridView` both call the shared calculator. Legacy grid **Summaries** remain a separate column-aggregate feature (whole-column SUM/AVG/COUNT row) |
| `DatabaseTypeChooser` | **Two APIs (documented)** | ImportExport `Infer(names, sampleRows)` is batch-only. Avalonia Common.Tools streaming chooser remains SoT for Excel/CSV UI until parity audit |
| Credentials / DualCredentialStore | **Scaffold (deferred)** | Ports only — does **not** migrate JBAG↔JBCG |
| Multi-DB ports | **Scaffold (deferred)** | Interfaces only |

## Definition of done for a shared surface

1. Logic lives in NetezzaSql packages.
2. Both hosts call it on a production path (or one host + documented deprecation).
3. Local duplicate removed or reduced to a UI adapter.
4. Unit tests cover the contract; import also has live tests when NZ_DEV_* is set.
5. Package is included in CI `dotnet pack`.

## Deferred (do not start without a product decision)

- `SqlExecutionRunner` adoption by Avalonia/Legacy orchestration stacks
- Core `SchemaCache` as a replacement for host schema repositories
- History / snippets / credential file formats
- Unifying Avalonia streaming `DatabaseTypeChooser` with ImportExport `Infer` without parity tests
