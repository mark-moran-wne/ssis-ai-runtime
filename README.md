# SSIS AI Runtime

A headless foundation for inspecting and safely operating on SSIS packages through the native SSIS runtime. The native package object remains authoritative; projections are intended for tools and AI context, not for serialization back into DTSX.

## Current Foundation

- `SsisAiRuntime.Core` targets .NET Standard 2.0 and defines package sessions, a generic loader contract, load results, and runtime diagnostics.
- `SsisAiRuntime.Ssis16` targets .NET Framework 4.8 and loads packages with SSIS 16 `Application.LoadPackage`.
- `SsisAiRuntime.Inspectors` targets .NET Standard 2.0 and defines runtime-neutral immutable inspector results, focused SQL/lineage/configuration contexts, session-scoped semantic handles, and a control-flow graph. The SSIS 16 adapter provides package, connection, variable, parameter, SQL task, expression-presence, executable hierarchy, and data-flow projections with column lineage/type metadata, runtime connection references, and allowlisted source/destination settings. Sensitive values are omitted or sanitized, and detailed reports expose incomplete/unsupported coverage.
- `SsisAiRuntime.Tests` tests the portable Core contract without requiring SSIS.
- `SsisAiRuntime.Cli` is a Windows .NET Framework 4.8 x64 console app over the existing loader and inspectors. Its first release is strictly read-only.

Additional inspectors, graph queries, an AI skill, and mutation workflows are planned follow-on work.

## Prerequisites

- .NET 10 SDK to build and run the test project.
- Windows with SSIS 16 installed to build and exercise the native adapter.
- The adapter resolves `Microsoft.SQLServer.ManagedDTS.dll` and `Microsoft.SqlServer.DTSPipelineWrap.dll` from the standard `160\DTS\Binn` directory or the SSIS 16 .NET Framework GAC location. `Microsoft.SqlServer.DTSRuntimeWrap.dll` is also required for data-flow column metadata and may be in the .NET Framework GAC_32 directory.

If the assemblies are installed elsewhere, provide their full paths through `SSIS16ManagedDtsPath`, `SSIS16PipelineWrapPath`, and `SSIS16RuntimeWrapPath`. The directory-based `SSIS16BinnPath` property is also supported.

## Build and Test

Run the portable Core tests on any supported .NET 10 SDK host:

```powershell
dotnet test .\tests\SsisAiRuntime.Tests\SsisAiRuntime.Tests.csproj -c Release
```

Build the complete solution on Windows with SSIS 16 installed:

```powershell
dotnet build .\SsisAiRuntime.sln -c Release
```

To use non-standard assembly locations, pass their full paths:

```powershell
dotnet build .\SsisAiRuntime.sln -c Release -p:SSIS16ManagedDtsPath="C:\path\to\Microsoft.SQLServer.ManagedDTS.dll" -p:SSIS16PipelineWrapPath="C:\path\to\Microsoft.SqlServer.DTSPipelineWrap.dll" -p:SSIS16RuntimeWrapPath="C:\path\to\Microsoft.SqlServer.DTSRuntimeWrap.dll"
```

Run the repeatable Windows smoke test against a representative package:

```powershell
.\tests\Run-SsisAiRuntime.Ssis16SmokeTest.ps1 -PackagePath "C:\path\to\Package.dtsx"
```

## Usage

Build the solution in Release, then invoke the built executable from PowerShell:

```powershell
$cli = '.\src\SsisAiRuntime.Cli\bin\Release\net48\SsisAiRuntime.Cli.exe'
& $cli all 'C:\path\to\Package.dtsx' --summary
& $cli overview 'C:\path\to\Package.dtsx'
& $cli sql 'C:\path\to\Package.dtsx'
& $cli lineage 'C:\path\to\Package.dtsx'
& $cli configuration 'C:\path\to\Package.dtsx'
$exitCode = $LASTEXITCODE
```

Commands accept a command and a package path, optionally followed by `--summary`. `overview` returns package metadata and root counts; `sql` returns SQL-task metadata and binding counts; `lineage` returns data-flow components, paths, columns, and lineage IDs; `configuration` returns connection, variable, parameter, and expression-presence metadata. These are projections of the existing services, not a new DTSX parser. SQL text and setting values are deliberately omitted in v1, even when the adapter has sanitized them.

`all` loads the package once, retains the four operation reports in memory, and returns them under `results.overview`, `results.sql`, `results.lineage`, and `results.configuration`. All reports share one session ID. Incomplete coverage does not stop later operations; a load or inspection failure does. `completedOperations` lists the operation reports produced, including a failed operation, and `skippedOperations` lists those not run. On an operation failure, the aggregate has `succeeded: false` but retains earlier reports in `results`. No wrapper is needed.

Use `--summary` for terminal and agent consumption. The executable checks the typed result and returns package identity plus `counts`, with at most eight coverage groups, five unsupported examples, and eight diagnostics. Summary metadata text is capped at 120 characters. `outputMode` is `summary`, and `coverage` contains exact `unsupportedCount`, `groupCount`, `reasonCounts`, `diagnosticCount`, and omitted group/example/diagnostic counts. The bounded `unsupportedItems` array contains examples, not the full coverage total. For `all --summary`, each operation has its own summary envelope and the outer `coverage` aggregates totals; the outer `unsupportedItems` is empty to avoid duplicating examples. Omit `--summary` to receive full redacted projections and coverage entries.

Every invocation writes one JSON document to stdout, including errors. Schema `1.0` uses camelCase property names, string enum values, and these stable envelope fields:

```json
{"schemaVersion":"1.0","command":"overview","succeeded":true,"isComplete":true,"exitCode":0,"results":{},"diagnostics":[],"unsupportedItems":[],"redaction":{"policy":"metadata-only","applied":true,"connectionStringsOmitted":true,"variableValuesOmitted":true,"parameterValuesOmitted":true,"expressionTextOmitted":true,"sqlTextOmitted":true,"settingValuesOmitted":true,"descriptionsOmitted":true,"diagnosticDetailsOmitted":true}}
```

In detailed mode, `results` is the package overview for `overview`; the other individual commands return a focused context with `kind`, `package`, `sqlStatements`, `dataFlows`, `connections`, `variables`, `parameters`, `expressions`, and `isComplete`. Collections outside the selected context are empty. Coverage gaps appear in the operation envelope's `unsupportedItems` (`id`, `name`, `creationName`, `reasonCode`, and a fixed redacted `reason`). Diagnostics contain `code`, `severity`, and a safe `message`. On an individual operation failure, `results` is null and `succeeded` and `isComplete` are false; on invalid arguments, `command` is also null. A successful inspection can be incomplete. Session IDs vary per load and are not persistent identifiers. The new fields and optional output modes extend schema `1.0` without changing existing detailed-command fields.

Coverage codes are allowlisted at the inspector boundary: `coverage.intentional_omission` means a value or detail is deliberately not exposed, `coverage.unsupported_metadata` means a projection or type is unsupported, `coverage.read_failed` means a metadata read or reference resolution failed, and `coverage.unspecified` preserves legacy items without guessing a cause. None exposes native reason or exception text. Intentional omissions still produce incomplete coverage and exit `5`; they do not mean the package is broken.

| Exit | Meaning |
| --- | --- |
| `0` | Inspection succeeded with complete coverage |
| `2` | Invalid arguments or unsupported command |
| `3` | Package missing, inaccessible, or failed to load |
| `4` | Inspection failure or unavailable runtime dependency |
| `5` | Inspection succeeded with unsupported/incomplete coverage |

For `all`, exit `0` means every operation completed with full coverage, exit `5` means at least one was incomplete but none failed, and exit `3` or `4` identifies the first failure, with earlier reports retained.

The CLI never executes, validates, saves, or edits packages. It omits connection strings, variable/parameter values, expression text, SQL text, data-flow setting values, descriptions, supplied paths, and native diagnostic/exception details. Object names and IDs remain visible for navigation; do not place secrets in metadata names. There is no raw-output or password option. Password-protected or unreadable packages may fail to load; packages with unavailable encrypted fields may produce partial metadata. This is inspection coverage, not a guarantee that a package will execute successfully.

In GitHub Copilot Chat, ask for the built executable directly:

> Run `src/SsisAiRuntime.Cli/bin/Release/net48/SsisAiRuntime.Cli.exe all "C:\path\to\Package.dtsx" --summary`. Use the executable directly, without a wrapper. Load and inspect only; never execute or modify it. Parse the JSON, handle exit 5 as incomplete inspection, and summarize counts, diagnostics, coverage reason codes, omitted-detail totals, and redaction status. Do not print SQL or secrets.

For repeatable Windows validation, run `tests/Run-SsisAiRuntime.Ssis16SmokeTest.ps1` as shown above. It builds into an isolated temporary directory, checks the direct inspectors in a fresh process, invokes all four CLI commands, compares counts, verifies JSON/redaction/error exits, and checks that the package's SHA-256 hash is unchanged. Use 64-bit PowerShell compatible with the installed SSIS runtime.

This is a standalone read-only CLI with a Copilot Chat invocation example, not a registered Copilot skill. A dedicated skill remains follow-on work.
