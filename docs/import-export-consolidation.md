# Import/Export Consolidation Status

This document tracks the consolidation of **import and export** logic shared by the two
hosts (Avalonia `JustyBase` and WinForms `JustyBase.Legacy`) into `JustyBase.ImportExport`.

Sibling documents: [shared-core-status.md](shared-core-status.md) covers `JustyBase.Core`
(execution, scripting, risk, export contracts); [authoring-shared-core.md](authoring-shared-core.md)
covers the SQL authoring engine. This document covers the import/export pipeline only.

## The boundary (single sentence)

**All import/export *engines* live in `JustyBase.ImportExport`; hosts keep only UI,
platform adapters (WinForms `IDataObject`/`Clipboard`, Avalonia `IClipboard`, per-host
Excel writers via SpreadSheetTasks) and host config plumbing.**

Excel-format reading/writing (`SpreadSheetTasks`) deliberately stays **host-backed** to
keep the shared packages AOT-compatible. The shared library covers CSV, named-pipe EXTERNAL
loads, DDL/type generation, type inference, and CSV/Parquet/JSON/compressed export.

## Source of truth decisions (confirmed)

| Surface | SoT | Notes |
| --- | --- | --- |
| Type detection (`DatabaseTypeChooser`) | **Avalonia streaming version** | Ported to a string-incremental engine that does not depend on SpreadSheetTasks; contract inspired by `justybase-vscode-private` (`DatabaseImportDataType`/`DatabaseColumnTypeChooser`/`DatabaseImportTypeMapper`). Legacy `ChooseTypes` and shared batch `Infer` become adapters. |
| CSV reader | Avalonia `CsvReader` (superset) | Legacy is Brotli-only; shared gains Gzip/Zstd, `TreatAllColumnsAsText`, Pesel/Regon-as-text. |
| CSV export | shared `CsvExportWriter` | Already used by both hosts. |
| Parquet export | shared `ParquetExportWriter` | Already used by Avalonia; not wired into Legacy yet. |
| Compression | shared `CompressedExportStreams` (Gzip/Zip) | LZ4/Brotli/Zstd remain host-backed until a shared codec is approved. |
| Excel read/write | host-backed | SpreadSheetTasks stays per host. |
| Encoding/newline resolution | shared resolver | Legacy had 3+ local copies; unified in `ExportEncodingResolver`. |

## Phase status

| Phase | Scope | Status |
| --- | --- | --- |
| 0.1 | This document + SoT policy | ✅ Done |
| 0.2 | Parity-test scaffolding (shared fixtures + host characterization) | 🔲 Pending — shared unit fixtures in progress; host characterization deferred |
| 0.3 | AOT/dependency verification for additions (Sylvan.Data.Csv, ZstdSharp) | ✅ Done — `Sylvan.Data.Csv` 1.4.4 + `ZstdSharp.Port` 0.8.8 added to `JustyBase.ImportExport`; `IsAotCompatible` build clean. |
| 1.1 | Unify `AdvancedExportOptions` (Avalonia parser + Legacy DTO) | ⏸️ Deferred — models diverge (Legacy carries xlsx in-place options; Avalonia carries `#directive` parser + PluginCommon `CompressionEnum`). Needs a shared `CompressionKind` mapping and a parity audit. |
| 1.2 | Shared `ExportEncodingResolver` (`ResolveEncoding`/`ResolveNewLine`) | ✅ Done — new `Export/ExportEncodingResolver.cs` (Avalonia aliases + Legacy codepage/name superset); Legacy copies removed from `CsvExportSettings`, `LegacyResultExportUseCase`, `SqlExecutionRouter`. BOM-based import-side `WinFormsImportOperationService.ResolveEncoding(requested, path)` intentionally kept (UI/dialog path). |
| 1.3 | Shared JSON export writer; Legacy `TabularTextExporter.WriteJson` delegates | ✅ Done — new `Export/JsonExportWriter.cs` (+ source-gen `ImportExportJsonContext`); `TabularTextExporter.WriteJson` delegates. `AppBase.Services.csproj` now conditionally uses local ProjectReference (`UseLocalJustyBaseLibraries`) mirroring `App.Data.Netezza`. |
| 1.4 | Remove obsolete `ParquetFileWriterFromDataReader` alias | ✅ Done — deleted from Avalonia `JustyBase.Common.Tools` (0 consumers). |
| 1.5 | Extract pure helpers from Legacy `ImportExportTasks.cs` monolith | 🔲 Pending — do incrementally behind tests |
| 2.1 | Shared `CsvReader` (compression-parameterized) | ✅ Done — `CsvRowReader` + pure `CsvCellTypeResolver` (`CsvCell`/`CsvCellKind`) in `JustyBase.ImportExport.Import`; Sylvan-backed, Brotli/Gzip/Zstd. Host `CsvReader`s (Avalonia + Legacy) are now thin `ExcelReaderAbstract` adapters. Tests in `CsvRowReaderTests.cs`. |
| 2.2 | Shared string-incremental `DatabaseTypeChooser` + contract | 🚧 Partly done (engine+contract+unit tests). `ImportTypeAnalyzer` + `ImportColumnKind`/`DetectedImportColumnType` in `JustyBase.ImportExport.Import` — string-incremental port of Avalonia `ChooseTypes` (header overrides, log10 numeric lengths, long-text fast path, `isTypeMix`, scale 6). Host rewiring (Avalonia `ExcelTypeDetection`, Legacy `ChooseTypes` → adapters) pending a characterization parity test. |
| 2.3 | Move `DbImportJob`, `DBReaderWithMessages`, `ImportUsingOptions` into shared | 🔲 Pending |
| 2.4 | Formalize Netezza import pipeline (pipe + DDL + `NetezzaImportHelper`) | 🔲 Pending |
| 3.x | Shared import orchestrator + `IDbFileImportEngine` abstraction + clipboard seam | 🔲 Pending |
| 4.x | Shared CSV/Parquet/compressed exporter; host Excel adapter; dedupe `ResultHelper` vs `ExportDbReaderExtensions` | 🔲 Pending |
| 5.x | Promote `IImportUseCase`/`IResultExportUseCase` + `ImportExportViewModel`; final `IImportProgress`/`IClipboardPayload` contracts | 🔲 Pending |

## Definition of done for a shared surface

1. Logic lives in the `JustyBase.NetezzaSql` packages.
2. Both hosts call it on a production path (or one host + documented deprecation).
3. Local duplicate removed or reduced to a UI adapter.
4. Unit tests cover the contract; parity tests compare host output vs shared output on the
   same fixtures (see `tests/JustyBase.NetezzaSql.Tests` and host test projects).
5. Package is included in CI `dotnet pack`; `Verify-Local.ps1` passes.

## Known residuals (tracked)

| Item | Status | Notes |
| --- | --- | --- |
| Legacy `AdvancedExportOptions` (xlsx in-place DTO) | Host | `TabName`, `PivotTable*`, `StartCell`, `ForceRefresh`, `Clear` exist only in Legacy and have no shared counterpart. |
| Excel writers | Host | Different Xlsx/Xlsb writers per host; shared `AdvancedExcelExportOptions` is the contract seam. |
| WinForms clipboard (`IDataObject`) / Avalonia `IClipboard` | Host | Shared seam (`IClipboardPayload`) deferred to Phase 3.3/5.2. |
| Generic formatter fallback / host progress dialogs | Host | Import progress UI stays host-side. |

## Verification gates

- `dotnet build` + unit tests for `JustyBase.NetezzaSql` and both host solutions after any
  shared change.
- Parity tests: `JustyBase.NetezzaSql.Tests/ImportExportCoreTests*`,
  `JustyBase.Tests/SharedCoreHostAdapterTests`, `JustData.UiTests/ImportExport*`,
  `AppBase.Tests/ImportExport/*`.
- `pwsh .\eng\Verify-Local.ps1` in `JustyBase.NetezzaSql` before pushing.
