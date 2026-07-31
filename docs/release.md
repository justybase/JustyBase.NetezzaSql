# Release guide

Publishing to NuGet.org is done by GitHub Actions when a GitHub Release is
**published**. The `Build & Publish` workflow packs with
`PackageVersion` taken from the release tag (leading `v` is stripped) and the
`publish` job pushes `.nupkg` / `.snupkg` via NuGet OIDC (`nuget/login`).

## Before cutting a release

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
4. Choose a SemVer prerelease (for example `0.3.0-preview.8`), bump
   `PackageVersion` in `Directory.Build.props`, and update `CHANGELOG.md`.
   Publish parser, DDL, catalog, `JustyBase.Netezza`, Core, and ImportExport
   under the **same** version.
5. Inspect the generated `.nupkg` and `.snupkg` files, including README, XML
   documentation, Apache-2.0 metadata and Source Link.
6. Commit, push to `master`, create a GitHub Release with tag
   `v<version>` (for example `v0.3.0-preview.8`). Watch the release-triggered
   workflow with `gh run watch`.

A failing coverage gate means the candidate is not a release candidate yet; do not
lower a threshold merely to pass.

Never place NuGet API keys, database credentials, or release tokens in this
repository or its workflow files. Trusted publishing uses repository secrets
such as `NUGET_USER` together with OIDC — not a long-lived API key in YAML.
