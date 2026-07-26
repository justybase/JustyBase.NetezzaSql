# Manual release guide

This repository never publishes packages from CI. CI only verifies source,
tests and package artifacts.

1. Run `dotnet restore .\JustyBase.NetezzaSql.sln`, then build and test the
   Release solution locally. The Release build treats compiler and xUnit
   analyzer warnings as errors.
2. Run `pwsh .\eng\Verify-Local.ps1` before every push to `master`. For a
   release candidate, use `pwsh .\eng\Verify-Local.ps1 -FullCi`.
   Per-library gates in `eng\Test-Coverage.ps1`: parser, DDL, catalog, and
   Netezza integration (80% line / 65% branch); LSP handlers (60% / 50%);
   **JustyBase.Core** and **JustyBase.ImportExport** (50% / 35%).
3. Pack the libraries, then run `pwsh .\eng\Test-PackageConsumer.ps1` to
   compile a fresh project that has only the generated `.nupkg` files as its
   JustyBase dependencies. In a workspace containing the unchanged Legacy
   consumer, also run `pwsh .\eng\Test-LegacyConsumer.ps1`.
4. Choose a SemVer prerelease, for example `0.2.0-preview.7`, and build each
   package with `/p:PackageVersion=<version>`. Publish parser, DDL, catalog,
   and `JustyBase.Netezza` under the **same** version.
5. Inspect the generated `.nupkg` and `.snupkg` files, including README, XML
   documentation, Apache-2.0 metadata and Source Link.
6. Commit the release notes, create and push the matching Git tag, then create
   the GitHub release yourself.
7. Upload the inspected packages to NuGet yourself.

A failing coverage gate means the candidate is not a release candidate yet; do not
lower a threshold merely to pass.

Never place NuGet API keys, database credentials, or release tokens in this
repository or its workflow files.
