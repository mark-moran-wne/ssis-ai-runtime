# SSIS AI Runtime

A headless foundation for inspecting and safely operating on SSIS packages through the native SSIS runtime. The native package object remains authoritative; projections are intended for tools and AI context, not for serialization back into DTSX.

## Current Foundation

- `SsisAiRuntime.Core` targets .NET Standard 2.0 and defines package sessions, a generic loader contract, load results, and runtime diagnostics.
- `SsisAiRuntime.Ssis16` targets .NET Framework 4.8 and loads packages with SSIS 16 `Application.LoadPackage`.
- `SsisAiRuntime.Inspectors` targets .NET Standard 2.0 and defines runtime-neutral immutable inspector results, focused SQL/lineage/configuration contexts, session-scoped semantic handles, and a control-flow graph. The SSIS 16 adapter provides package, connection, variable, parameter, SQL task, expression-presence, executable hierarchy, and data-flow projections with column lineage/type metadata, runtime connection references, and allowlisted source/destination settings. Sensitive values are omitted or sanitized, and detailed reports expose incomplete/unsupported coverage.
- `SsisAiRuntime.Tests` tests the portable Core contract without requiring SSIS.

Additional inspectors, graph queries, a CLI, an AI skill, and mutation workflows are planned follow-on work.

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

Reference `SsisAiRuntime.Ssis16` from a Windows .NET Framework 4.8 console application, then load a package and request read-only projections:

```csharp
using System;
using System.Linq;
using SsisAiRuntime.Ssis16;

internal static class Program
{
	private static int Main(string[] args)
	{
		if (args.Length != 1)
		{
			Console.Error.WriteLine("Usage: SsisAiRuntime.Sample <package.dtsx>");
			return 2;
		}

		var loadResult = new PackageLoader().Load(args[0]);
		foreach (var diagnostic in loadResult.Diagnostics.Items)
		{
			Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
		}

		if (!loadResult.Succeeded)
		{
			return 1;
		}

		var session = loadResult.Session;
		var overview = new PackageOverviewInspector().InspectDetailed(session).Items[0];
		var connections = new PackageConnectionInspector().InspectDetailed(session);
		var variables = new PackageVariableInspector().InspectDetailed(session);
		var parameters = new PackageParameterInspector().InspectDetailed(session);
		var executables = new PackageExecutableInspector().InspectDetailed(session);
		var sql = new PackageSqlInspector().InspectDetailed(session);
		var expressions = new PackageExpressionInspector().InspectDetailed(session);
		var dataFlows = new PackageDataFlowInspector().InspectDetailed(session);
		var contextBuilder = new PackageContextBuilder();
		var sqlContext = contextBuilder.BuildSql(overview, sql);
		var lineageContext = contextBuilder.BuildLineage(overview, dataFlows);
		var configurationContext = contextBuilder.BuildConfiguration(overview, connections, variables, parameters, expressions);
		var handles = new SemanticHandleCatalogBuilder().Build(
			overview,
			connections.Items,
			variables.Items,
			parameters.Items,
			executables.Items,
			dataFlows.Items);
		var precedence = new PackagePrecedenceInspector().InspectDetailed(session);
		var controlFlow = new ControlFlowGraphBuilder().Build(executables.Items, precedence, handles);

		Console.WriteLine($"Package: {overview.PackageName}");
		Console.WriteLine($"SSIS runtime: {loadResult.Diagnostics.RuntimeVersion} ({loadResult.Diagnostics.ProcessArchitecture})");
		Console.WriteLine($"Connections: {connections.Items.Count}");
		Console.WriteLine($"Variables: {variables.Items.Count}");
		Console.WriteLine($"Parameters: {parameters.Items.Count}");
		Console.WriteLine($"Executables: {executables.Items.Count} (complete: {executables.IsComplete})");
		Console.WriteLine($"SQL statements: {sql.Items.Count}");
		Console.WriteLine($"Expression owners: {expressions.Items.Count} (complete: {expressions.IsComplete})");
		Console.WriteLine($"Data flows: {dataFlows.Items.Count} (unsupported details: {dataFlows.UnsupportedItems.Count})");
		Console.WriteLine($"Data-flow input columns: {dataFlows.Items.Sum(flow => flow.Components.Sum(component => component.InputColumns.Count))}");
		Console.WriteLine($"Data-flow output columns: {dataFlows.Items.Sum(flow => flow.Components.Sum(component => component.OutputColumns.Count))}");
		Console.WriteLine($"Data-flow external columns: {dataFlows.Items.Sum(flow => flow.Components.Sum(component => component.ExternalMetadataColumns.Count))}");
		Console.WriteLine($"Data-flow runtime connections: {dataFlows.Items.Sum(flow => flow.Components.Sum(component => component.RuntimeConnections.Count))}");
		Console.WriteLine($"Focused contexts: SQL {sqlContext.SqlStatements.Count}, lineage {lineageContext.DataFlows.Count}, configuration {configurationContext.Connections.Count} connections");
		Console.WriteLine($"Semantic handles: {handles.Entries.Count}");
		Console.WriteLine($"Control-flow graph: {controlFlow.Nodes.Count} nodes, {controlFlow.Edges.Count} edges (complete: {controlFlow.IsComplete})");

		foreach (var executable in executables.Items)
		{
			Console.WriteLine($"{new string(' ', executable.Depth * 2)}{executable.Name} ({executable.CreationName})");
		}

		foreach (var statement in sql.Items)
		{
			Console.WriteLine(statement.StatementText);
		}

		return 0;
	}
}
```

The inspectors return immutable metadata, not live native objects. Connection strings, parameter/variable values, and expression text are omitted. SQL comments and string literals are sanitized. Detailed results include completeness and unsupported-item reporting. This example loads and inspects the package; it does not execute or modify it. Run the application with an architecture compatible with SSIS 16 and the package's providers and custom components.
