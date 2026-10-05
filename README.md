# SSIS AI Runtime

A headless foundation for inspecting and safely operating on SSIS packages through the native SSIS runtime. The native package object remains authoritative; projections are intended for tools and AI context, not for serialization back into DTSX.

## Current Foundation

- `SsisAiRuntime.Core` targets .NET Standard 2.0 and defines package sessions, a generic loader contract, load results, and runtime diagnostics.
- `SsisAiRuntime.Ssis16` targets .NET Framework 4.8 and loads packages with SSIS 16 `Application.LoadPackage`.
- `SsisAiRuntime.Tests` tests the portable Core contract without requiring SSIS.

Inspectors, graph queries, a CLI, an AI skill, and mutation workflows are planned follow-on work; they are not implemented yet.

## Prerequisites

- .NET 10 SDK to build and run the test project.
- Windows with SSIS 16 installed to build and exercise the native adapter.
- The SSIS 16 `Microsoft.SQLServer.ManagedDTS.dll` assembly, normally under `C:\Program Files\Microsoft SQL Server\160\DTS\Binn`.

If SSIS is installed in a different location, provide its `DTS\Binn` directory through the `SSIS16BinnPath` MSBuild property.

## Build and Test

Run the portable Core tests on any supported .NET 10 SDK host:

```powershell
dotnet test .\tests\SsisAiRuntime.Tests\SsisAiRuntime.Tests.csproj -c Release
```

Build the complete solution on Windows with SSIS 16 installed:

```powershell
dotnet build .\SsisAiRuntime.sln -c Release
```

The SSIS adapter must run with an architecture compatible with the installed SSIS runtime and any package providers or custom components.
