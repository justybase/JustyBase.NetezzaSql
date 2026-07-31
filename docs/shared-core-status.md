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
| Schema cache | **Scaffold (deferred)** | TTL string snapshots do not replace host schema repositories |
| Schema context menu SQL templates | **Production (partial)** | Core `SchemaContextMenuCatalog` is shared SQL SoT (`Ids.*`, `Format`). Avalonia/Legacy keep richer UI trees and host-only actions |
| History / snippets / session vars persistence | **Scaffold (deferred)** | In-memory Core API only; hosts keep file/DB persistence |
| Grid `CellStatsCalculator` | **Production (Avalonia)** | Typed numeric **selection** stats. Legacy grid **Summaries** are column aggregates — not the same feature; deferred until a selection-stats UI exists |
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
- Wiring Core `CellStatsCalculator` into Legacy unless a selection-stats UI path is added
