# Shared Core Status

This document tracks which `JustyBase.Core` / `JustyBase.ImportExport` surfaces are **production** vs **scaffold**.

| Area | Status | Notes |
| --- | --- | --- |
| Risk (`SqlRiskAnalysisService`, `SqlRiskGate`) | **Production** | Consumed by Avalonia RUN/lint and Legacy Application wrapper |
| Import SQL + USING builder | **Production** | Always emits `REMOTESOURCE 'dotnet'` (client-side DATAOBJECT/pipe) |
| Named-pipe import executor | **Production** | `NetezzaPipeImportExecutor` used by Avalonia helper + Legacy Fast CSV |
| Script dialect (`AvaloniaScriptDialect`, Legacy adapter) | **Production (partial)** | Avalonia `SasMacroPreprocessor` + Legacy `SpecialCommandService`; full Legacy `__SessionVar__` eval still host-local |
| CSV export (`CsvExportWriter`) | **Production** | Legacy `TabularTextExporter` + Avalonia uncompressed CSV path |
| Excel / Parquet / compression export | **Host-backed** | Remains in Avalonia `ExportDbReaderExtensions` until packaged with ImportExport |
| Execution runner (`SqlExecutionRunner`) | **Scaffold** | Contracts ready; hosts still use local runners. Not production. |
| Schema cache / context menu | **Scaffold→improving** | Cache + catalog templates exist; Avalonia/Legacy still use richer local catalogs |
| History / snippets / session vars | **Scaffold** | In-memory only; hosts keep persistence |
| Grid `CellStatsCalculator` | **Production-ready API** | Typed numeric stats; hosts may still use local calculators until wired |
| Credentials / DualCredentialStore | **Scaffold** | Ports only — does **not** migrate JBAG↔JBCG |
| Multi-DB ports | **Scaffold** | Interfaces only |

## Definition of done for a shared surface

1. Logic lives in NetezzaSql packages.
2. Both hosts call it on a production path (or one host + documented deprecation).
3. Local duplicate removed or reduced to a UI adapter.
4. Unit tests cover the contract; import also has live tests when NZ_DEV_* is set.
5. Package is included in CI `dotnet pack`.
