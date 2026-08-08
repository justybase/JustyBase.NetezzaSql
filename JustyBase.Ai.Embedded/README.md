# JustyBase.Ai.Embedded

[![NuGet](https://img.shields.io/nuget/v/JustyBase.Ai.Embedded)](https://www.nuget.org/packages/JustyBase.Ai.Embedded/)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](../LICENSE)

Embedded llama.cpp (`llama-server`) GGUF model management for FIM inline completion and AI chat in JustyBase hosts.

This package lives in the [JustyBase.NetezzaSql](https://github.com/justybase/JustyBase.NetezzaSql) repository alongside the parser, DDL, and catalog libraries.

Use this package when you need to:

- Download and manage GGUF model files for embedded `llama-server`
- Start/stop a local llama.cpp server process for Fill-In-Middle (FIM) completion
- Report FIM model hardware capabilities (e.g. automatic GPU offload layer count)
- Interact with the local server over HTTP for inline completion requests

The package is pure managed code (`HttpClient`, `Process`, `System.Text.Json` source generation) — no native packages.

## Dependencies

| Package | Purpose |
|---------|---------|
| `JustyBase.Ai` | UI-agnostic AI chat logic and completion contracts |

## Target framework

- .NET 10
- Native AOT compatible (`IsAotCompatible = true`)

## Build and test

```powershell
dotnet restore .\JustyBase.NetezzaSql.sln
dotnet build .\JustyBase.NetezzaSql.sln -c Release
dotnet test .\tests\JustyBase.Ai.Tests\JustyBase.Ai.Tests.csproj -c Release
```

See the repository [release guide](../docs/release.md) for publishing.
