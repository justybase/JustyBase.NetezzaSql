# Manual release guide

This repository never publishes packages from CI. CI only verifies source,
tests and package artifacts.

1. Run `dotnet restore .\JustyBase.NetezzaSql.sln`, then build and test the
   Release solution locally. The Release build treats compiler and xUnit
   analyzer warnings as errors.
2. Run `pwsh .\eng\Assert-PublicApi.ps1` and
   `pwsh .\eng\Test-Coverage.ps1`. Parser, DDL, and catalog require at least
   90% line and 80% branch coverage. LSP protocol/handler code has its own
   70% line and 60% branch gate; the executable composition root is excluded.
3. Pack the libraries, then run `pwsh .\eng\Test-PackageConsumer.ps1` to
   compile a fresh project that has only the generated `.nupkg` files as its
   JustyBase dependencies. In a workspace containing the unchanged Legacy
   consumer, also run `pwsh .\eng\Test-LegacyConsumer.ps1`.
4. Choose a SemVer prerelease, for example `0.1.0-preview.1`, and build each
   package with `/p:PackageVersion=<version>`.
5. Inspect the generated `.nupkg` and `.snupkg` files, including README, XML
   documentation, Apache-2.0 metadata and Source Link.
6. Commit the release notes, create and push the matching Git tag, then create
   the GitHub release yourself.
7. Upload the inspected packages to NuGet yourself. Publish the parser, DDL
   and catalog packages under the same version.
8. Build and test `JustyBase.Netezza` against those exact package versions,
   then publish its matching package manually.

A failing coverage or API baseline gate means the candidate is not a release
candidate yet; do not lower a threshold or refresh a baseline merely to pass.

Never place NuGet API keys, database credentials, or release tokens in this
repository or its workflow files.
