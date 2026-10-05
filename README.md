# SSIS AI Runtime

A headless foundation for inspecting and safely operating on SSIS packages through the native SSIS runtime. The native package object remains authoritative; projections are intended for tools and AI context, not for serialization back into DTSX.

## Current Foundation

- `SsisAiRuntime.Core` targets .NET Standard 2.0 and defines package sessions, a generic loader contract, load results, and runtime diagnostics.
- `SsisAiRuntime.Ssis16` targets .NET Framework 4.8 and loads packages with SSIS 16 `Application.LoadPackage`.
- `SsisAiRuntime.Inspectors` targets .NET Standard 2.0 and defines runtime-neutral immutable inspector results. The SSIS 16 adapter currently provides a read-only package overview.
- `SsisAiRuntime.Tests` tests the portable Core contract without requiring SSIS.

Additional inspectors, graph queries, a CLI, an AI skill, and mutation workflows are planned follow-on work.

## Prerequisites

- .NET 10 SDK to build and run the test project.
- Windows with SSIS 16 installed to build and exercise the native adapter.
- Windows with SSIS 16 installed. The adapter resolves `Microsoft.SQLServer.ManagedDTS.dll` from the standard `160\DTS\Binn` directory or the SSIS 16 .NET Framework GAC location.

If the assembly is installed elsewhere, provide its full path through the `SSIS16ManagedDtsPath` MSBuild property. The older directory-based `SSIS16BinnPath` property is also supported.

## Build and Test

Run the portable Core tests on any supported .NET 10 SDK host:

```powershell
dotnet test .\tests\SsisAiRuntime.Tests\SsisAiRuntime.Tests.csproj -c Release
```

Build the complete solution on Windows with SSIS 16 installed:

```powershell
dotnet build .\SsisAiRuntime.sln -c Release
```

To use a non-standard assembly location, pass its full path:

```powershell
dotnet build .\SsisAiRuntime.sln -c Release -p:SSIS16ManagedDtsPath="C:\path\to\Microsoft.SQLServer.ManagedDTS.dll"
```

The SSIS adapter must run with an architecture compatible with the installed SSIS runtime and any package providers or custom components.
