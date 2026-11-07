# JustyBase.Ai.Contracts

[![NuGet](https://img.shields.io/nuget/v/JustyBase.Ai.Contracts)](https://www.nuget.org/packages/JustyBase.Ai.Contracts/)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](../LICENSE)

Lightweight, dependency-free contracts shared by JustyBase hosts and the optional
AI packages.

The package contains only plain .NET types:

- Chat session/message/attachment models (`JustyBase.Ai.Models`)
- Chat modes and tool-call models (`JustyBase.Ai.Models`)
- Host ports such as `IUiDispatcher` (`JustyBase.Ai.Ports`)
- The `IGitCommitMessageAiService` abstraction and its null implementation
  (`JustyBase.Ai.Git`)
- `SqlExecutionErrorStore`, the SQL-execution error hand-off used by non-AI code
  (`JustyBase.Ai.Services`)

It has **no** NuGet dependencies and does not reference `JustyBase.Ai`,
`JustyBase.Ai.Embedded`, `GitHub.Copilot.SDK`, `Microsoft.Extensions.AI`, or
`OpenAI`. Hosts that build without AI support can reference this package and
still share the same chat model contracts with AI-enabled hosts.

## Target framework

- .NET 10
- Native AOT compatible

## Build

```powershell
dotnet build .\JustyBase.Ai.Contracts\JustyBase.Ai.Contracts.csproj -c Release
```
