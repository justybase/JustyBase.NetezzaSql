# JustyBase.Ai

[![NuGet](https://img.shields.io/nuget/v/JustyBase.Ai)](https://www.nuget.org/packages/JustyBase.Ai/)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](../LICENSE)

UI-agnostic AI chat logic for JustyBase hosts: chat service, tool executor, OpenAI-compatible and Codex backends, prompt building, and chat model contracts.

This package lives in the [JustyBase.NetezzaSql](https://github.com/justybase/JustyBase.NetezzaSql) repository alongside the parser, DDL, and catalog libraries.

Use this package when you need to:

- Run a chat session against OpenAI-compatible or Codex backends
- Build FIM (Fill-In-Middle) and chat prompts with context and budget handling
- Execute and approve local SQL patch tools
- Switch chat model backends at runtime
- Render chat streams / markdown in a UI-agnostic way

The package has no UI dependency and is Native AOT compatible.

## Dependencies

| Package | Purpose |
|---------|---------|
| `JustyBase.Ai.Embedded` | Embedded llama.cpp (`llama-server`) GGUF model management for FIM and local chat |
| `DiffPlex` | Diff generation for SQL patch approval flows |
| `Microsoft.Extensions.AI` | Chat client abstractions |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | DI abstractions for service registration |

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
