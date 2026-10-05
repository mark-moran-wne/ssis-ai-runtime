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
```

## Native Integration Tests

Run the native C# verification on Windows with SSIS 16 installed:

```console
dotnet build .\tests\SsisAiRuntime.Ssis16IntegrationTests\SsisAiRuntime.Ssis16IntegrationTests.csproj -c Release
.\tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe
```

This separate x64 .NET Framework 4.8 test executable creates a fresh temporary package through the SSIS object model, configures only metadata for an integer-to-string Data Conversion, saves and reloads that scratch file, and checks the inspector, query, and actual CLI subprocess in both directions. It verifies redaction and an unchanged fixture hash after inspection, then removes the scratch directory. It never executes or validates a package, configures a database connection, or alters caller-supplied packages. Fixture creation is test-only; the production CLI remains strictly read-only and has no EzAPI dependency.

The same test executable verifies the shared `IDTSExpressionEvaluatorEx100.Parse` column-reference observer and Derived Column mappings. Successful native lookups are column evidence, not variable/parameter bindings. Separate scope fixtures verify heuristic reference resolution, owner identity, shadowing, project-context gating, redaction, and unchanged hashes. Test-only syntax probes do not evaluate expressions. Record verification outcomes in [HISTORY.md](HISTORY.md), rather than treating a previous host result as a guarantee for a new installation.

A `CS8012` warning can occur with a GAC_32 interop reference. Confirm the verifier runs as x64 on the target host. Project-parameter resolution is covered by portable explicit-inventory tests; the native verifier does not provide a project-backed SSIS fixture.

The SQL scratch fixture uses stock Execute SQL tasks and metadata-only SQL Server OLE DB connections to verify connection-scoped object identities, parsed evidence, dynamic SQL gaps, and actual CLI context behavior. It checks metadata-only defaults, explicit SQL/expression sanitized-text opt-in, literal/comment redaction, option rejection, and unchanged hashes. It does not acquire a database connection, execute SQL, or validate a package.

## Package Smoke Test

Pass an optional existing package to the same C# verifier for read-only smoke checks:

```console
tests\SsisAiRuntime.Ssis16IntegrationTests\bin\Release\net48\SsisAiRuntime.Ssis16IntegrationTests.exe "C:\path\to\Package.dtsx"
```

After its scratch-fixture tests, the verifier loads the supplied package, compares direct inspector and CLI overview/SQL/lineage/configuration counts and coverage, checks aggregate/graph output, rejects execution commands, verifies missing-file exits and redaction, and compares the package's SHA-256 before and after inspection. It prints aggregate status only, not raw JSON or package values. It never executes, validates, saves, or modifies the supplied package. Build first, then invoke the test executable directly; no script wrapper is required.