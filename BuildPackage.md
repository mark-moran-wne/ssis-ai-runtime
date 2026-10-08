# Build and Test SSIS AI Runtime

This guide is for contributors building the executable and running verification. For runtime prerequisites, commands, JSON output, and safety limits, see [README.md](README.md).

The built CLI runs on Windows with .NET Framework 4.8 and SSIS 16; running it does not require a modern .NET SDK. This guide builds the existing executable, not a ZIP or release bundle.

## Build Prerequisites

- .NET 10 SDK for the complete solution and the portable `net10.0` test project.
- Windows with SSIS 16 installed to build and exercise the native adapter, CLI, and integration-test executable.
- The adapter resolves `Microsoft.SQLServer.ManagedDTS.dll` and `Microsoft.SqlServer.DTSPipelineWrap.dll` from the standard `160\DTS\Binn` directory or the SSIS 16 .NET Framework GAC location. `Microsoft.SqlServer.DTSRuntimeWrap.dll` is also required for data-flow column metadata and may be in the .NET Framework GAC_32 directory.

If the assemblies are installed elsewhere, provide their full paths through `SSIS16ManagedDtsPath`, `SSIS16PipelineWrapPath`, and `SSIS16RuntimeWrapPath`. The directory-based `SSIS16BinnPath` property is also supported. These are build-reference settings, not a replacement for installing the native runtime and required providers/components.

Run the commands below from the repository root.

## Build

Build the complete solution on Windows with SSIS 16 installed:

```console
dotnet build .\SsisAiRuntime.sln -c Release
```

To use non-standard assembly locations, pass their full paths:

```console
dotnet build .\SsisAiRuntime.sln -c Release -p:SSIS16ManagedDtsPath="C:\path\to\Microsoft.SQLServer.ManagedDTS.dll" -p:SSIS16PipelineWrapPath="C:\path\to\Microsoft.SqlServer.DTSPipelineWrap.dll" -p:SSIS16RuntimeWrapPath="C:\path\to\Microsoft.SqlServer.DTSRuntimeWrap.dll"
```

The CLI output is `src/SsisAiRuntime.Cli/bin/Release/net48/SsisAiRuntime.Cli.exe`, together with its configuration and application dependencies. Invoke the executable directly as described in [README.md](README.md#usage).

The inspector project generates its bounded expression grammar during builds through `Antlr4BuildTasks`; `Antlr4.Runtime.Standard` is an application dependency. Grammar and candidate extraction remain runtime-neutral, separate from native scope projection and native column observation. First-time builds may need to acquire ANTLR generator tooling in addition to NuGet packages.

`Microsoft.SqlServer.TransactSql.ScriptDom` is an application dependency for bounded T-SQL 160 analysis and token-based SQL context redaction. Keep it with the CLI dependencies. The SQL parser never opens a database connection and the context builder never calls an LLM.

## Portable Tests

Run the runtime-neutral Core, inspector, query, and CLI-contract tests on any supported .NET 10 SDK host. These tests do not require SSIS:

```console
dotnet test .\tests\SsisAiRuntime.Tests\SsisAiRuntime.Tests.csproj -c Release
dotnet test .\tests\SsisAiRuntime.Corpus.Tests\SsisAiRuntime.Corpus.Tests.csproj -c Release
dotnet test .\tests\SsisAiRuntime.Mutations.Tests\SsisAiRuntime.Mutations.Tests.csproj -c Release
```

## Native Integration Tests

Run the native C# verification on Windows with SSIS 16 installed:

```console
dotnet build .\tests\SsisAiRuntime.Ssis16IntegrationTests\SsisAiRuntime.Ssis16IntegrationTests.csproj -c Release
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe
```

By default, this separate x64 .NET Framework 4.8 test executable creates a fresh temporary package through the SSIS object model, configures only metadata for an integer-to-string Data Conversion, saves and reloads that scratch file, and checks the inspector, query, and actual CLI subprocess in both directions. It verifies redaction and an unchanged fixture hash after inspection, then removes the scratch directory. The default mode never executes or validates a package, acquires a database connection, or alters caller-supplied packages. Fixture creation is test-only; the production CLI remains strictly read-only and has no EzAPI dependency.

The same test executable verifies the shared `IDTSExpressionEvaluatorEx100.Parse` column-reference observer and Derived Column mappings. Successful native lookups are column evidence, not variable/parameter bindings. Separate scope fixtures verify heuristic reference resolution, owner identity, shadowing, project-context gating, redaction, and unchanged hashes. Test-only syntax probes do not evaluate expressions. Record verification outcomes in [HISTORY.md](HISTORY.md), rather than treating a previous host result as a guarantee for a new installation.

An additional test-only fixture constructs a source-to-destination data flow entirely in memory. It remaps a destination input between two source columns, inserts a Derived Column into the established flow, reconnects the paths, and maps the calculated output into a second destination column. Native `Package.SaveToXML` serialization is checked structurally for column types, expression references, path endpoints, destination mappings, and unchanged source metadata. `Package.LoadFromXML` reloads the edited result into another in-memory package. This fixture creates no files and acquires no database connection; it does not execute or validate a package. It probes native editing behavior, not a production mutation service or permission to execute mutation plans.

A `CS8012` warning can occur with a GAC_32 interop reference. Confirm the verifier runs as x64 on the target host. Project-parameter resolution is covered by portable explicit-inventory tests; the native verifier does not provide a project-backed SSIS fixture.

The SQL scratch fixture uses stock Execute SQL tasks and metadata-only SQL Server OLE DB connections to verify connection-scoped object identities, parsed evidence, dynamic SQL gaps, and actual CLI context behavior. It checks metadata-only defaults, explicit SQL/expression sanitized-text opt-in, literal/comment redaction, option rejection, and unchanged hashes. It does not acquire a database connection, execute SQL, or validate a package.

## Opt-In Execution Probe

The default and execution harnesses also test developer `describe`, shared `catalog validate`, and structural `compare` commands. Portable comparison tests check namespace/format normalization, actual metadata changes, meaningful whitespace, generated-ID visibility, bounded examples and DTD/depth refusal. Named `probe` execution tests verify recipe matching, native component diagnostics, expected/actual mismatch details and cleanup reporting. These developer diagnostics intentionally differ from the inspection CLI's redacted output.

Execute the fixed synthetic row-flow recipe explicitly, or run its native regression checks:

```console
.\src\SsisAiRuntime.FlowRunner\bin\Release\net48\SsisAiRuntime.FlowRunner.exe components
.\src\SsisAiRuntime.FlowRunner\bin\Release\net48\SsisAiRuntime.FlowRunner.exe demo
type request.json | .\src\SsisAiRuntime.FlowRunner\bin\Release\net48\SsisAiRuntime.FlowRunner.exe run
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe --execute-flow-probe
```

Build the solution or integration project first. The opt-in mode calls the real FlowRunner executable and checks demo and configurable arithmetic/conditional expressions, exact values and row counts, expected-value mismatch, malformed expressions, incompatible request versions, JSON redaction, invalid-command rejection, and cleanup. It runs separately from the default metadata harness and cannot be combined with a caller-supplied package. The runner uses local synthetic CSV input/output files only, a child-process timeout, and installed Flat File Source/Destination and Derived Column components. No LocalDB, credentials, or database connection is needed. This is trusted developer execution under the caller's permissions, not OS-level isolation. See [src/SsisAiRuntime.FlowRunner/README.md](src/SsisAiRuntime.FlowRunner/README.md) for the JSON request format and limits. Portable tests compile the request parser without native SSIS references and test bounds, versioning, types, duplicate/unknown fields, and immutability.

## Mutation Host Lifecycle Probe

Run the separate opt-in RenameTask lifecycle suite:

```console
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe --mutation-host-probe
```

This mode calls the actual MutationHost on scratch DTSX files, uses a test checkpoint artifact copy and metadata-only validator doubles, and checks rename/save/reload/publication plus refusal/cleanup paths. It covers nested/event-handler targets, stale names and hashes, missing requirements/targets, blocking coverage, checkpoint failure, injected save/reload failure, validator exceptions, unexpected semantic changes, destination races, source locking, and cancellation. It does not invoke native validation or package execution and cannot be combined with a caller-supplied package. A passed suite verifies orchestration, not a production checkpoint or native-validator implementation. See [src/SsisAiRuntime.MutationHost/README.md](src/SsisAiRuntime.MutationHost/README.md).

## Required RenameTask Gate

Before merging changes to native executable discovery, rename preview/coverage policy, corpus verification, artifact staging, or MutationHost execution, run the following from the repository root on Windows x64 with SSIS 16 and the .NET 10 SDK installed. The native host cannot be certified by portable tests alone.

```console
dotnet build .\SsisAiRuntime.sln -c Release
dotnet test .\tests\SsisAiRuntime.Tests\SsisAiRuntime.Tests.csproj -c Release --no-build
dotnet test .\tests\SsisAiRuntime.Corpus.Tests\SsisAiRuntime.Corpus.Tests.csproj -c Release --no-build
dotnet test .\tests\SsisAiRuntime.Mutations.Tests\SsisAiRuntime.Mutations.Tests.csproj -c Release --no-build
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe --mutation-host-probe
```

Every command must exit `0`. The lifecycle suite must report 22 passed cases, including a checkpoint task that is still pending when returned and resumes on another thread, plus refusal of duplicate native IDs in a deliberately malformed scratch artifact. Malformed XML construction is test-only and is never the production editing path. The suite checks source preservation and absence of staging files for every case. When adding a case, update its expected count and documented coverage; do not delete or relax an existing case simply to obtain a pass.

The checkpoints and validators remain controlled test implementations. A pass proves the bounded orchestration and native rename/save/reload path, not production checkpoint storage, native validation, or arbitrary mutation safety. Record the tested commit, SSIS version/architecture, and command outcomes in the PR or review evidence. If no SSIS-enabled host is available, report the gate as unverified, not passed. This is a documented merge requirement, not a configured GitHub required check; an SSIS-enabled CI runner and branch-protection setup are still pending. FlowRunner execution changes additionally require `--execute-flow-probe`.

## Required Column Resizing Gate

Before merging changes to the column-resize plan, Quick Analysis, coordinated metadata editor, or copy-edit lifecycle, run on Windows x64 with SSIS 16 and the .NET 10 SDK:

```console
dotnet build .\SsisAiRuntime.sln -c Release
dotnet test .\tests\SsisAiRuntime.Tests\SsisAiRuntime.Tests.csproj -c Release --no-build
dotnet test .\tests\SsisAiRuntime.Corpus.Tests\SsisAiRuntime.Corpus.Tests.csproj -c Release --no-build
dotnet test .\tests\SsisAiRuntime.Mutations.Tests\SsisAiRuntime.Mutations.Tests.csproj -c Release --no-build
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe --mutation-host-probe
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe --column-resize-probe
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe --execute-flow-probe
```

Every command must exit `0`. The resize suite must report 15 passed cases. Quick Analysis is bounded to 1..10,000 rows and reports the maximum observed length without returning values. Shrink preview requires a matching successful analysis and explicit acknowledgement; a target below the observed maximum is refused. The confirmation warns that future values may be longer even after a complete scan, and that a partial scan leaves unscanned rows unchecked. The analyzed data-file hash is checked again before execution. These scratch tests do not call native `Validate` or `Execute`; the FlowRunner execution probe separately covers real synthetic data-flow execution. The checkpoint and validator are test doubles, not production services. Resizing never changes physical database DDL and only publishes a separate DTSX copy after verification. This is a documented merge gate, not a configured CI check.

## Designer Layout Probe

Run the focused designer-layout parser, placement, comparison, and native topology checks on Windows x64 with SSIS 16:

```console
dotnet build .\tests\SsisAiRuntime.Ssis16IntegrationTests\SsisAiRuntime.Ssis16IntegrationTests.csproj -c Release
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe --create-designer-fixtures .\tests\LayoutFixtures
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe --designer-layout-probe
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe --inspect-designer-fixtures .\tests\LayoutFixtures
```

The generator refuses to overwrite any fixture and verifies native load/reload task/component/path counts. Its packages deliberately have no layout coordinates. In SSDT, arrange the horizontal/vertical, branch, merge, and crossed-path cases, save each package, then run the inspection command. It reports native owner/node/port IDs, positions, route-point counts, and fixed diagnostics; it does not validate or execute packages. Preserve an untouched generated copy if you want a before/after comparison. Review the SSDT-saved fixtures before checking them into `tests/LayoutFixtures`; persistence remains unverified until that real round-trip is inspected.

The parser/placement/comparer test mode is:

```console
dotnet build .\tests\SsisAiRuntime.Ssis16IntegrationTests\SsisAiRuntime.Ssis16IntegrationTests.csproj -c Release
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe --designer-layout-probe
```

The current native fixture has no designer-authored layout property; the probe verifies explicit refusal rather than fabricated positions. Do not claim real-package layout extraction is verified until a designer-authored saved fixture confirms native load/save/reload behavior and exact ID correlation. This is a documented follow-up gate, not a configured CI check.

## Package Smoke Test

The `components` command above performs metadata discovery only and does not request execution. Both default and opt-in native harness modes compare its complete catalog with native registration enumeration, check deterministic ordering and capability flags, and reject execution options on the discovery command.

Pass an optional existing package to the same C# verifier for read-only smoke checks:

```console
tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe "C:\path\to\Package.dtsx"
```

After its scratch-fixture tests, the verifier loads the supplied package, compares direct inspector and CLI overview/SQL/lineage/configuration counts and coverage, checks aggregate/graph output, rejects execution commands, verifies missing-file exits and redaction, and compares the package's SHA-256 before and after inspection. It prints aggregate status only, not raw JSON or package values. It never executes, validates, saves, or modifies the supplied package. Build first, then invoke the test executable directly; no script wrapper is required.