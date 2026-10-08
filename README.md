# SSIS AI Runtime (Experimental)

A headless toolkit for inspecting SSIS packages and testing synthetic data flows through the native SSIS runtime. The inspection CLI stays read-only; a separate development harness constructs and executes contrived flows and probes coordinated metadata edits. The native package object remains authoritative; projections are intended for tools and AI context, not for serialization back into DTSX.

## Milestone Progress

Current local implementation as of 2026-10-07. Implemented does not imply general production mutation support or verification on every SSIS installation.

| Area | Status | Current Scope |
| --- | --- | --- |
| Native runtime and inspection CLI | Implemented | SSIS 16 x64 loading, metadata inspection, safe JSON, and coverage reporting |
| Dependency and control-flow graphs | Implemented | Native identity, selectors, impact queries, and projected precedence relationships |
| Expression and SQL dependencies | Implemented | Native column observations, heuristic lexical/scope bindings, and parsed T-SQL evidence |
| Corpus baselines and fingerprints | Implemented | Versioned snapshots, diff/verify/approve, canonical SHA-256 fingerprints, and transient-ID normalization |
| Mutation contracts and requirements | Implemented | Read-only RenameTask preview, explicit lifecycle statuses, and schema 1.1 obligations; no executor |
| Artifact staging | Implemented, limited | Separate destination, verification-gated publication, overwrite refusal, and normal-failure cleanup; not a complete save workflow |
| Synthetic flow execution | Implemented | Native Derived Column, Int32-to-Int16/Int64 Data Conversion, and Unicode Flat File text probes with output assertions |
| Coordinated column resizing | Bounded lifecycle verified | Exact-ID resize analysis and copy-edit plan supports grow/shrink; add/remove remain in-memory schema edits |
| Designer layout | Implemented, limited | Native-ID-scoped snapshot, deterministic placement and comparison; designer-authored layout serialization still needs a fixture |
| Shared component catalog | Foundation implemented | 59 seeded SSIS 16 definitions and local matching; discovery is distinct from runnable recipe support |
| Component support tooling and diagnostics | Implemented, bounded | Native describe, recipe probes, structural XML compare, catalog validation, native errors and output mismatch details; catalog PR guide remains pending |
| Real-package editing lifecycle | Deferred | No agent-facing copy-edit command, checkpoint/restore host, live table DDL, or complete save/reload/validation/diff executor |

`SsisAiRuntime.MutationHost` now has a scratch-package RenameTask lifecycle probe (22 native cases) with controlled checkpoint and validation implementations. It verifies native rename, save-as, reload, semantic checks, asynchronous checkpoint continuation, duplicate-ID refusal, and publication/refusal paths; production services and agent-facing commands remain deferred. Rename-scoped verification permits known task-property omissions without hiding coverage or claiming complete corpus fingerprints. Changes affecting this slice must pass the [required regression gate](BuildPackage.md#required-renametask-gate). See [src/SsisAiRuntime.MutationHost/README.md](src/SsisAiRuntime.MutationHost/README.md).

Column resizing currently covers grow/shrink on a direct Flat File Source-to-Flat File or OLE DB Destination path. `AnalyzeColumnResize` scans the source file configured on the selected connection and reports rows scanned, maximum observed width (UTF-16 code units for `DT_WSTR`, encoded bytes for `DT_STR`), and whether EOF was reached; it never returns values or paths. `PreviewColumnResize` binds that analysis to the package, native selectors, target width, and input-file hash. Shrink requires successful analysis and explicit user acknowledgement. A target below the observed maximum is refused; a partial scan warns that unseen or future data may be longer. The analysis and file hash are checked again before execution. The host writes a separate copy, checkpoints the original, and verifies coordinated native metadata changes; it does not alter physical database schema or invoke native Validate/Execute. Add/remove remain in-memory schema edits. Production checkpoint and validator implementations and an editing CLI remain deferred. See [src/SsisAiRuntime.MutationHost/README.md](src/SsisAiRuntime.MutationHost/README.md), [BuildPackage.md](BuildPackage.md#required-column-resizing-gate), and [Plan.md](Plan.md) for boundaries and verification.

## Current Foundation

- `SsisAiRuntime.Core` targets .NET Standard 2.0 and defines package sessions, a generic loader contract, load results, and runtime diagnostics.
- `SsisAiRuntime.Ssis16` targets .NET Framework 4.8 and loads packages with SSIS 16 `Application.LoadPackage`.
- `SsisAiRuntime.Inspectors` targets .NET Standard 2.0 and defines runtime-neutral immutable inspector results, focused SQL/lineage/configuration contexts, session-scoped semantic handles, and a control-flow graph. The SSIS 16 adapter provides package, connection, variable, parameter, SQL task, expression-presence, executable hierarchy, and data-flow projections with column lineage/type metadata, runtime connection references, and allowlisted source/destination settings. Sensitive values are omitted or sanitized, and detailed reports expose incomplete/unsupported coverage.
- `SsisAiRuntime.AI` targets .NET Standard 2.0 and provides deterministic read-only tools over inspector projections. It does not call an LLM or load SSIS packages; the SSIS host composes one immutable snapshot per package session.
- `SsisAiRuntime.Corpus` targets .NET Standard 2.0 and projects analysis snapshots into versioned dependency/evidence baselines with compatibility checks, structural comparison, and deterministic semantic fingerprints.
- `SsisAiRuntime.Mutations` targets .NET Standard 2.0 and provides native-ID-only `RenameTask` preview, versioned deterministic contracts, and explicit execution lifecycle statuses. It has no executor or native package mutation implementation.
- `SsisAiRuntime.MutationHost` targets .NET Framework 4.8 x64 and composes scratch-tested RenameTask and bounded column-resize copy-edit lifecycles, plus isolated designer-layout extraction, placement, and comparison services. No production checkpoint/validator implementations or agent-facing edit commands are provided.
- `SsisAiRuntime.Tests` tests the portable Core contract without requiring SSIS.
- `SsisAiRuntime.Corpus.Tests` exercises snapshot projection, baseline validation, and corpus comparison without requiring SSIS.
- `SsisAiRuntime.Mutations.Tests` exercises mutation target validation, graph-based impact, and incomplete-coverage refusal without requiring SSIS.
- `SsisAiRuntime.Cli` is a Windows .NET Framework 4.8 x64 console app over the existing loader and inspectors. Its first release is strictly read-only.
- `SsisAiRuntime.FlowRunner` is a separate trusted-developer SSIS 16 x64 execution executable. `components` discovers installed registrations and matches shared definitions; `demo` and JSON-stdin `run` execute contrived flows. Recipes cover Derived Column expressions, numeric Data Conversion, and Unicode text width checks. Native column-edit methods coordinate in-memory changes. This is a development harness, not a security sandbox or a service for executing real jobs. See [src/SsisAiRuntime.FlowRunner/README.md](src/SsisAiRuntime.FlowRunner/README.md).
- [catalog/components/ssis16.json](catalog/components/ssis16.json) contains shared component definitions, not package corpus data or machine paths. Definitions do not automatically authorize execution or create configuration recipes.

A personal package-inspection skill exists outside this repository; a repository-registered Copilot skill and mutation execution workflows remain planned follow-on work.

## Runtime Requirements

- Windows x64 with .NET Framework 4.8.
- SSIS 16 installed, with the providers and custom components needed by the inspected package. Only SSIS 16 x64 has been verified.
- The built CLI and its application dependencies kept together.

Contributor documentation: [BuildPackage.md](BuildPackage.md). Investigation results and dated verification: [HISTORY.md](HISTORY.md).

## Developer Flow Harness

Run these commands from the repository root after building. Unlike the inspection commands below, `demo` and `run` explicitly execute a synthetic SSIS package. They use disposable local CSV files, expected-output assertions, a child worker with a 60-second timeout, and automatic cleanup. They do not accept a real package path, database connection string, or credentials. There is no fixture-retention option.

```console
src\SsisAiRuntime.FlowRunner\bin\Release\net48\SsisAiRuntime.FlowRunner.exe components
src\SsisAiRuntime.FlowRunner\bin\Release\net48\SsisAiRuntime.FlowRunner.exe describe microsoft.derived-column
src\SsisAiRuntime.FlowRunner\bin\Release\net48\SsisAiRuntime.FlowRunner.exe catalog validate
src\SsisAiRuntime.FlowRunner\bin\Release\net48\SsisAiRuntime.FlowRunner.exe demo
type request.json | src\SsisAiRuntime.FlowRunner\bin\Release\net48\SsisAiRuntime.FlowRunner.exe run
```

Schema `1.0` Derived Column requests remain supported. Schema `1.1` selects `derived-column`, `data-conversion`, or `flat-file-text`. A coordinated width-repair example:

```json
{
	"schemaVersion": "1.1",
	"recipe": "flat-file-text",
	"values": ["Long student name"],
	"expectedValues": ["Long student name"],
	"sourceWidth": 4,
	"destinationWidth": 4,
	"widenTo": 64
}
```

The probe builds the narrow flow, widens the connection managers and connected source/destination metadata, executes SSIS, and verifies that the full string survives. Source truncation is a real native failure. Delimited Flat File destinations can write values beyond their declared width, so the harness explicitly rejects narrower destination metadata rather than claiming native destination truncation.

The shared catalog and installed inventory are separate: this host has 59 registrations, but only four selected components currently have execution recipes. `executionTestAvailable` describes test coverage; listing components does not execute them or certify them on another machine.

`NativeFlatFileColumnEditor` provides in-memory `Widen`, `Shrink`, `Add`, and `Remove` methods. Shrinking and removal require explicit data-loss acknowledgement. Addition/removal currently support direct Unicode delimited Flat File source-to-destination flows; arbitrary intermediate transformations and branches are outside that scope. OLE DB destination changes affect SSIS external-column metadata and mappings only, not a database table's actual schema. These methods are not yet exposed as commands that load and save caller-supplied packages.

The developer runner also exposes `probe <recipe>`, `compare` over before/after XML, and `catalog validate <file>` for candidate definitions. Native descriptions expose defaults and ports, not guessed complete configuration requirements. See the [runner guide](src/SsisAiRuntime.FlowRunner/README.md#component-support-tooling) for formats and limits.

The default native test harness stays non-executing; `--execute-flow-probe` opts into real synthetic row execution. Build/test commands and prerequisites are in [BuildPackage.md](BuildPackage.md). Developer diagnostics include native SSIS messages, component names and expected/actual mismatches; they may contain synthetic values, expressions and temporary paths. Cleanup failures preserve the original failure. The inspection CLI retains its separate metadata-only redaction policy.

To create seven synthetic designer-layout packages for an SSDT round-trip, run `SsisAiRuntime.Ssis16IntegrationTests.exe --create-designer-fixtures <directory>`. They cover a single component, horizontal and vertical two-component flows, branch, merge, crossed paths, and nested data flows. They contain no coordinates or live connections; arrange them in SSDT and save before inspecting the persisted layout with `--inspect-designer-fixtures <directory>`. See [BuildPackage.md](BuildPackage.md#designer-layout-probe) for the verification workflow and current format limitation.

## Usage

Invoke the executable directly. The example below uses the Release output location.

Running without arguments returns exit `2` and includes this usage in the `cli.usage` diagnostic:

```text
Usage:
	  SsisAiRuntime.Cli.exe <overview|sql|lineage|configuration|control-flow> <package.dtsx> [--details]
	  SsisAiRuntime.Cli.exe inspect <package.dtsx> [--include <overview|sql|lineage|configuration>[,...]]... [--details]
	  Repeat --include or comma-separate report names; omit it to include all four reports.
	SsisAiRuntime.Cli.exe trace <package.dtsx> --flow <id> --component <id> --column <id> [--direction upstream|downstream] [--details]
	SsisAiRuntime.Cli.exe <predecessors|successors> <package.dtsx> --task <id> [--recursive] [--details]
	SsisAiRuntime.Cli.exe search <package.dtsx> --query <text> [--kind <object-kind>] [--details]
	  SsisAiRuntime.Cli.exe ai <package.summary|dependency.graph> <package.dtsx>
	  SsisAiRuntime.Cli.exe ai dependency.query <package.dtsx> --node <node-key> [--recursive]
	  SsisAiRuntime.Cli.exe ai impact.analysis <package.dtsx> --node <node-key>
	  SsisAiRuntime.Cli.exe ai selector.resolve <package.dtsx> --selector <text> [--kind <object-kind>]
	  SsisAiRuntime.Cli.exe ai impact.classified <package.dtsx> --node <node-key>
	  SsisAiRuntime.Cli.exe ai question.plan <question>
	  SsisAiRuntime.Cli.exe ai context <package.dtsx> [--include-sanitized-text]
	  SsisAiRuntime.Cli.exe ai corpus.snapshot <package.dtsx>
	  SsisAiRuntime.Cli.exe ai corpus.diff <package.dtsx> --baseline <file>
	  SsisAiRuntime.Cli.exe ai corpus.verify <package.dtsx> --baseline <file>
	  SsisAiRuntime.Cli.exe ai corpus.approve <package.dtsx> --baseline <file> [--upgrade]

Output defaults to a bounded summary. Use --details for the full redacted projection.
```

```bat
cd src\SsisAiRuntime.Cli\bin\Release\net48
SsisAiRuntime.Cli.exe inspect "C:\path\to\Package.dtsx"
SsisAiRuntime.Cli.exe inspect "C:\path\to\Package.dtsx" --include sql,lineage
SsisAiRuntime.Cli.exe inspect "C:\path\to\Package.dtsx" --include sql --include configuration --details
SsisAiRuntime.Cli.exe inspect "C:\path\to\Package.dtsx" --details
SsisAiRuntime.Cli.exe overview "C:\path\to\Package.dtsx"
SsisAiRuntime.Cli.exe sql "C:\path\to\Package.dtsx"
SsisAiRuntime.Cli.exe lineage "C:\path\to\Package.dtsx"
SsisAiRuntime.Cli.exe configuration "C:\path\to\Package.dtsx"
```

Run the first example from the repository root. The following examples assume the same Command Prompt remains in the executable directory.

### AI Analysis Tools

The `ai` routes expose bounded, deterministic read-only tools over the same inspector projections. `dependency.query` and `impact.analysis` accept node keys returned by `dependency.graph`; `question.plan` selects an intent only and never resolves package objects or runs a planned tool.

```bat
SsisAiRuntime.Cli.exe ai package.summary "C:\path\to\Package.dtsx"
SsisAiRuntime.Cli.exe ai dependency.graph "C:\path\to\Package.dtsx"
SsisAiRuntime.Cli.exe ai dependency.query "C:\path\to\Package.dtsx" --node "Connection:connection-id" --recursive
SsisAiRuntime.Cli.exe ai impact.analysis "C:\path\to\Package.dtsx" --node "Connection:connection-id"
SsisAiRuntime.Cli.exe ai selector.resolve "C:\path\to\Package.dtsx" --selector "Warehouse" --kind Connection
SsisAiRuntime.Cli.exe ai impact.classified "C:\path\to\Package.dtsx" --node "Connection:connection-id"
SsisAiRuntime.Cli.exe ai question.plan "What uses this connection?"
```

### Corpus Baselines

Corpus snapshots persist the package dependency graph, allowlisted edge evidence, and aggregated coverage gaps in a versioned JSON baseline. They omit raw SQL, expressions, settings, values, and absolute package paths. Node and edge inventories are deterministically ordered; the generated timestamp is baseline metadata.

```bat
SsisAiRuntime.Cli.exe ai corpus.snapshot "C:\path\to\Package.dtsx"
SsisAiRuntime.Cli.exe ai corpus.approve "C:\path\to\Package.dtsx" --baseline ".\baselines\package.json"
SsisAiRuntime.Cli.exe ai corpus.diff "C:\path\to\Package.dtsx" --baseline ".\baselines\package.json"
SsisAiRuntime.Cli.exe ai corpus.verify "C:\path\to\Package.dtsx" --baseline ".\baselines\package.json"
```

`corpus.approve` creates or replaces the named baseline. Review changes before approving them; the command does not save or modify the SSIS package. `corpus.diff` reports added, removed, and metadata-changed nodes, edge changes, package identity/name changes, and coverage-gap deltas. `corpus.verify` returns exit `0` when the snapshot matches and is complete, `5` when it matches but has coverage gaps, and `4` with `corpus.verify.mismatch` when it differs. Schema major versions must match; use `--upgrade` on `corpus.approve` to explicitly replace an incompatible baseline with the current schema. Baseline files may contain package names, IDs, and dependency structure; review access controls before committing or publishing them. See [BuildPackage.md](BuildPackage.md#native-integration-tests) for the native CLI lifecycle verification.

Selector resolution checks exact keys, exact native IDs, exact names, then bounded partial-name matches; ambiguous results return candidates without choosing one. Classified impact includes shortest projected paths to consumers. Variable and parameter expression dependencies use ANTLR lexical candidates plus deterministic resolution against native declaration IDs and container hierarchy. Only unique resolutions produce `UsesVariable` or `UsesParameter` edges, labeled with `evidence: "LexicalAndScopeResolved"` in graph/impact output and bounded AI facts. These are heuristic relationships, not native parser bindings. These routes do not execute, validate, or modify packages.

Variable resolution stops at the nearest matching scope; same-scope duplicates and unwrapped names matching multiple namespaces remain ambiguous. Package parameters resolve only in the package inventory. Project parameters require explicitly supplied project metadata through the hosting API; standalone CLI loads report `expression.project_context_unavailable`. Parse failures, missing owners/targets, invalid topology, and ambiguous/missing references remain redacted `expression.*` coverage gaps without guessed edges or echoed reference tokens. The grammar is bounded, not a claim of complete SSIS language compatibility. Raw expression text and values never enter CLI output or AI context; sanitized syntax requires the separate context opt-in below.

Supported unwrapped variable candidates use simple names such as `@Counter`. Use wrapped forms for namespaces and parameters, such as `@[User::Value]` and `@[$Package::X]`; extended unwrapped forms are unsupported. The broader column/function identifier token does not imply broader unwrapped-variable syntax. Native probe evidence is in [HISTORY.md](HISTORY.md#unwrapped-syntax-probe).

Commands accept a command and a package path. Every command returns a bounded summary by default; add `--details` to return the full redacted projection. The focused commands cover package overview, SQL-task metadata, data-flow lineage, and connections/variables/parameters/expressions. These are projections of the existing services, not a new DTSX parser. SQL text and setting values remain omitted from these commands, even when sanitized syntax was collected separately.

### Parsed SQL Dependencies

Direct-input Execute SQL tasks on identified SQL Server connections are analyzed internally before sanitization using ScriptDom's T-SQL 160 parser. SQL Server OLE DB providers and SqlClient connection metadata establish the supported dialect; unknown providers, variable/file sources, and expression-generated statements remain coverage gaps. Parsing does not connect to the database, prove object existence, or classify tables versus views.

The dependency graph uses connection-scoped `SchemaObject` nodes with exact supplied server/database/schema/name parts. Missing qualifiers are not filled with `dbo` or an assumed database, and object identities are not case-folded without catalog collation metadata. Edges are `ReadsSchemaObject`, `WritesSchemaObject`, `ExecutesSchemaObject`, or `ReferencesSqlFunction`, with `ParsedSchemaObject`, `ParsedExecuteTarget`, or `ParsedFunctionReference` evidence. Select nodes by graph key using the existing selector/impact tools; schema objects are not added to the separate semantic-handle metadata search catalog.

Comments, string literals, CTE names, temporary tables, and table variables do not become persistent schema-object dependencies. Dynamic SQL, `sp_executesql`, context changes, unresolved write aliases/CTEs, unsupported constructs, and malformed statements produce fixed `sql.*` coverage gaps. Failed parses emit no provisional references. This is bounded syntactic discovery, not complete SQL binding or transitive stored-procedure analysis; optional classification requires explicit catalog metadata and is not implemented.

### LLM Context

```bat
SsisAiRuntime.Cli.exe ai context "C:\path\to\Package.dtsx"
SsisAiRuntime.Cli.exe ai context "C:\path\to\Package.dtsx" --include-sanitized-text
```

The default response is metadata-only. The explicit flag enables a `sanitized-context-opt-in` policy and adds `sanitizedTexts` beside bounded dependency nodes/edges, control-flow facts, and lineage facts. It is accepted only by `ai context`, not by other analysis commands. There are no embedded LLM calls; clients decide whether to send this context to a model.

SQL and expression strings/numeric literals are masked through parser tokens, SQL comments are removed, and malformed or unsupported text is omitted entirely with `context.text_unavailable`. SQL double-quoted content is masked conservatively. At most 20 snippets are returned, each at most 4,096 characters after sanitization; metadata/fact collections are capped at 100 with omission counts. Snippets are labeled `SanitizedSyntaxOnly` and `untrusted-package-content`: they are not executable, semantically equivalent to the original, or dependency evidence.

Identifiers and metadata names remain visible, so do not place secrets in those fields. Treat all package content as untrusted data, never instructions. The LLM may interpret supplied evidence but must preserve coverage gaps and cannot invent edges or omitted literal values. These controls reduce exposure; they are not a guarantee that arbitrary user-authored identifiers contain no secrets.

`inspect` loads the package once and returns selected reports under their matching keys in `results`. By default it runs overview, SQL, lineage, and configuration; use repeatable `--include` options or a comma-separated list to select a subset, such as `--include sql,lineage`. The selected reports retain canonical order regardless of argument order. All reports share one session ID. Incomplete coverage does not stop later operations; a load or inspection failure does. `completedOperations` lists selected reports produced, including a failed report, and `skippedOperations` lists selected reports not run after a failure. On an operation failure, the aggregate has `succeeded: false` but retains earlier reports in `results`. No wrapper is needed.

The executable checks the typed result and returns package identity plus `counts`, with at most eight coverage groups, five unsupported examples, and eight diagnostics. Summary metadata text is capped at 120 characters. `outputMode` is `summary`, and `coverage` contains exact `unsupportedCount`, `groupCount`, `reasonCounts`, `diagnosticCount`, and omitted group/example/diagnostic counts. The bounded `unsupportedItems` array contains examples, not the full coverage total. For `inspect`, each selected operation has its own summary envelope and the outer `coverage` aggregates only those selected reports; the outer `unsupportedItems` is empty to avoid duplicating examples. Add `--details` when you need full redacted projections and coverage entries.

### Column Tracing

Use `lineage --details` to select a data flow's `executableId`, a component's `id`, and an input/output column's `id`. These are native metadata IDs, not names or the numeric `lineageId`. Then invoke the executable directly:

```bat
SsisAiRuntime.Cli.exe trace "C:\path\to\Package.dtsx" --flow "flow-executable-id" --component "component-id" --column "column-id" --direction downstream
SsisAiRuntime.Cli.exe trace "C:\path\to\Package.dtsx" --flow "flow-executable-id" --component "component-id" --column "column-id" --direction upstream --details
```

Direction defaults to `downstream`. Summary output gives package identity and counts for flow ID, direction, columns, and links. Add `--details` to return `package` and `trace`, including reached `columns` and `links`, with the selected column first. Link kinds are `Path` for a projected pipeline path, `ProjectedIdentity` for an explicitly projected same-lineage relationship inside a component, and `SynchronousPassThrough` for a proven buffer-column relationship through an output's declared synchronous input. Synchronous links retain the `synchronousOutputId`.

Components also expose `outputs` (including `synchronousInputId` and `isErrorOutput`) and runtime `virtualInputColumns`. Virtual columns describe available input-buffer metadata, including columns the component does not select for use. Their `id` is a projection key, `virtual:<input-port-id>:<lineage-id>`, not a fabricated SSIS native column ID. Pass the complete key to `--column`; when the same port/lineage is already a selected input, tracing uses that canonical input node. Buffer presence does not prove that a component consumes the value or writes it to a destination. Output relationships must match the projected ports and lineage; unresolved synchronous references remain gaps, and no metadata refresh or package validation is performed to resolve them. Known outputs with no downstream path are terminal rather than an assumed transformation gap.

Tracing matches positive lineage IDs and exact component/port endpoints, not column names. It follows branches and protects against cycles. If a transformation's column mapping, a path's matching column, or a valid lineage ID is not exposed, it stops that branch and reports `coverage.unsupported_metadata` with exit `5`. It never invents mappings between differently numbered columns or reconstructs expressions. Missing or ambiguous flow/column selections return exit `4` with `lineage.selection.invalid` and no echoed selector values.

Built-in Data Conversion output columns can expose `sourceInputLineageId`, taken only from their unencrypted, positive integer `SourceInputColumnLineageID` property. The adapter recognises the component through the installed runtime's creation names and registered class IDs, not its package display name. A uniquely resolved source creates an `ExplicitMapping` link even when the output has a different name, type, or lineage ID. Missing or ambiguous sources stay incomplete; an invalid explicit source cannot fall back to a coincidentally matching lineage ID. Successful numeric mapping properties are no longer counted as omitted configuration values.

Recognised built-in Derived Column outputs expose `expressionDependencies`, containing `isResolved`, `resolution` (`NativeParser` or `Unresolved`), and sorted `inputLineageIds`. SSIS's native parser resolves each unencrypted `Expression` in the data-flow task's variable scope through a read-only forwarding column collection; expression text, variable values, and native parser error text never enter the projection. Expressions over 65,536 characters, unavailable parser interfaces, unexpected generic metadata reads, parse failures, and missing/ambiguous input references remain explicit gaps. Only successfully resolved references create `ExpressionResolved` links. A resolved empty input list means no input-column dependency; it does not imply the expression is independent of variables.

In-place replacements appear as distinct output-stage projections with `isReplacement: true` and an ID of `replaced:<native-input-column-id>:<output-port-id>`. Pass that complete key to `--column` to trace the computed replacement. Expression edges originate from original input nodes, including when an expression references the column being replaced; the old value is never silently passed through as the new one. Both ordinary and constant replacements are covered. Other expressions in the same component still reference original inputs, not another replacement's computed value.

This remains a bounded implementation: arbitrary custom component expression contracts are not exposed. Variable/parameter references in registered Derived Column expressions are separately projected into the package dependency graph using heuristic lexical-and-scope evidence; column tracing continues to use only native column observations. No package validation, execution, or metadata refresh is used for extraction. Fixture and package verification results are in [HISTORY.md](HISTORY.md).

Trace completeness applies only to the selected projected relationships, not to unrelated configuration omissions or execution validity. `inspect` returns the four overview/SQL/lineage/configuration contexts; a selected-column trace is a separate operation.

### Control-Flow Queries

Inspect the native executable hierarchy and precedence graph, then select a task by its `nativeId` from `results.graph.nodes`:

```bat
SsisAiRuntime.Cli.exe control-flow "C:\path\to\Package.dtsx" --details
SsisAiRuntime.Cli.exe predecessors "C:\path\to\Package.dtsx" --task "task-native-id"
SsisAiRuntime.Cli.exe successors "C:\path\to\Package.dtsx" --task "task-native-id" --recursive
```

Detailed results contain `package` and `graph`, with semantic-handle-backed `nodes`, `edges`, and `isComplete`. The full graph contains both `Containment` and `Precedence` edges. Predecessor/successor results include the selected task first and follow only precedence edges: immediate neighbours by default, transitively with `--recursive`. Cycles are bounded by a visited set; duplicate task names do not affect ID selection. Missing or ambiguous task IDs return exit `4` with `controlflow.selection.invalid`, without echoing the supplied selector.

Summary output returns package identity and counts for `nodes`, `precedenceEdges`, and `containmentEdges`. Node counts include the selected task. Use `control-flow --details` when you need graph nodes and their native IDs. Use native IDs between CLI invocations; semantic handles remain session-scoped. Queries preserve graph coverage gaps rather than hiding unresolved endpoints. They describe potential precedence relationships, not actual execution order: expressions, constraint conditions, loops, disabled tasks, and parallel execution are not evaluated. Expression text, task-specific values, and package secrets remain omitted. The `inspect` command returns the four focused reports; control-flow queries are separate.

### Metadata Search

```bat
SsisAiRuntime.Cli.exe search "C:\path\to\Package.dtsx" --query "load"
SsisAiRuntime.Cli.exe search "C:\path\to\Package.dtsx" --query "warehouse" --kind Connection --details
SsisAiRuntime.Cli.exe search "C:\path\to\Package.dtsx" --query "task-native-id" --kind Executable
```

Search uses case-insensitive literal substrings, not regular expressions, over catalog object names, creation names, semantic handle values, and available native IDs. The query must contain 1 to 256 characters. Optional `--kind` accepts a `SemanticObjectKind` name, such as `Connection`, `Executable`, `DataFlowComponent`, or `OutputColumn`; numeric enum values are rejected. Duplicate names return all matching objects rather than choosing one.

Summary output returns match counts rather than match details. Add `--details` for `package`, exact `totalMatches`, `matchesOmitted`, and up to 50 deterministic `matches`, each with a semantic `reference` and available `nativeIds`. Some kinds have no catalogued native ID; data-flow component/column IDs also need their containing flow context. Use executable native IDs with task-query commands; handles remain session-scoped. The query text is not returned as a report field, and a zero-match search is successful.

The index covers catalogued package, connection, variable, parameter, task, data-flow component, selected input/output/external column, path, and runtime-connection metadata. It does not search SQL or expression text, descriptions, connection strings, variable/parameter/setting values, or unselected virtual-buffer columns. A missing match means no indexed metadata match, not proof that a value or object is absent from every package detail. Metadata read failures remain coverage gaps. `inspect` does not include a search operation.

Every invocation writes one JSON document to stdout, including errors. Schema `1.0` uses camelCase property names, string enum values, and these stable envelope fields:

```json
{"schemaVersion":"1.0","command":"overview","succeeded":true,"isComplete":true,"exitCode":0,"results":{},"diagnostics":[],"unsupportedItems":[],"redaction":{"policy":"metadata-only","applied":true,"connectionStringsOmitted":true,"variableValuesOmitted":true,"parameterValuesOmitted":true,"expressionTextOmitted":true,"sqlTextOmitted":true,"settingValuesOmitted":true,"descriptionsOmitted":true,"diagnosticDetailsOmitted":true}}
```

By default, `results` contains the bounded summary and `outputMode` is `summary`; `--details` returns the full projection. In detailed mode, `results` is the package overview for `overview`; the other individual commands return a focused context with `kind`, `package`, `sqlStatements`, `dataFlows`, `connections`, `variables`, `parameters`, `expressions`, and `isComplete`. Collections outside the selected context are empty. Coverage gaps appear in the operation envelope's `unsupportedItems` (`id`, `name`, `creationName`, `reasonCode`, and a fixed redacted `reason`). Diagnostics contain `code`, `severity`, and a safe `message`. On an individual operation failure, `results` is null and `succeeded` and `isComplete` are false; on invalid arguments, `command` is also null. A successful inspection can be incomplete. Session IDs vary per load and are not persistent identifiers. The optional detailed mode retains the detailed result fields.

Coverage codes are allowlisted at the inspector boundary: `coverage.intentional_omission` means a value or detail is deliberately not exposed, `coverage.unsupported_metadata` means a projection or type is unsupported, `coverage.read_failed` means a metadata read or reference resolution failed, and `coverage.unspecified` preserves legacy items without guessing a cause. None exposes native reason or exception text. Intentional omissions still produce incomplete coverage and exit `5`; they do not mean the package is broken.

| Exit | Meaning |
| --- | --- |
| `0` | Inspection succeeded with complete coverage |
| `2` | Invalid arguments or unsupported command |
| `3` | Package missing, inaccessible, or failed to load |
| `4` | Inspection failure or unavailable runtime dependency |
| `5` | Inspection succeeded with unsupported/incomplete coverage |

For `inspect`, exit `0` means every operation completed with full coverage, exit `5` means at least one was incomplete but none failed, and exit `3` or `4` identifies the first failure, with earlier reports retained.

The CLI never executes, validates, saves, or edits packages. It omits connection strings, variable/parameter values, raw expression/SQL text, data-flow setting values, descriptions, supplied paths, and native diagnostic/exception details. Only `ai context --include-sanitized-text` exposes bounded sanitized syntax under its distinct policy. Object names and IDs remain visible for navigation; do not place secrets in metadata names. There is no raw-output or password option. Password-protected or unreadable packages may fail to load; packages with unavailable encrypted fields may produce partial metadata. This is inspection coverage, not a guarantee that a package will execute successfully.

In GitHub Copilot Chat, ask Copilot to inspect a package with the built CLI:

> Inspect `"C:\path\to\Package.dtsx"` with `src\SsisAiRuntime.Cli\bin\Release\net48\SsisAiRuntime.Cli.exe inspect`. Use its default summary output, treat exit 5 as incomplete coverage, and report counts, diagnostics, coverage reason codes, omitted-detail totals, and redaction status. Inspect only; never execute or modify the package, and do not print SQL or secrets.

For repeatable native fixture and package smoke checks, see [BuildPackage.md](BuildPackage.md#native-integration-tests).

This is a standalone read-only CLI with a Copilot Chat invocation example, not a registered Copilot skill. A dedicated skill remains follow-on work.
