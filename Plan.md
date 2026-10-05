# Plan: Headless SSIS AI Runtime

Evolve the repository into a headless AI tool backed by the native SSIS runtime. SSIS 16 is the first supported runtime; SSIS 15 should be addable as a separate adapter. Build deterministic application services first, expose them through a CLI, then add a read-only AI skill. Defer mutation until inspection, diagnostics, and semantic diffs are dependable. The legacy WinForms project and solution have been removed at the user's request.

## Implementation Status (2026-10-04)

- Phase 1 foundation is implemented: `SsisAiRuntime.sln`, `netstandard2.0` Core, a `net48` SSIS 16 adapter, and `net10.0` Core tests.
- Core defines `PackageSession<TPackage>`, `IPackageLoader<TPackage>`, `PackageLoadResult<TPackage>`, and immutable runtime diagnostics. The native SSIS package remains available on the session.
- The SSIS 16 adapter uses `Application.LoadPackage` and avoids exposing exception messages or package paths in load-failure diagnostics.
- Windows Release solution build succeeds without a path override. The project resolves ManagedDTS and pipeline wrappers from `160\DTS\Binn` first, then their v16 GAC locations (including `DTSRuntimeWrap` in GAC_32), and supports explicit full-path overrides.
- A Windows smoke test loaded a local `.dtsx` package through `PackageLoader`: runtime assembly `16.0.0.0`, process architecture `x64`, zero diagnostics.
- Core tests pass on Windows: 4 passed, 0 failed. Earlier macOS Core test results were also 4 passed, 0 failed.
- On this Windows host, the v16 assembly is in the GAC, not in `160\DTS\Binn` or `160\SDK\Assemblies`. The project now has a GAC fallback and an explicit full-DLL-path override; both the default no-override build and the earlier explicit-path build succeeded.
- The native package API exposes overview candidates including `Name`, `ID`, `Description`, `CreationDate`, creator metadata, version fields, `ProtectionLevel`, `PackageType`, `Connections`, `Variables`, `Executables`, `PrecedenceConstraints`, and `Parameters`.
- Phase 2 has started: `SsisAiRuntime.Inspectors` defines a runtime-neutral immutable `PackageOverview` and generic inspector contract; the SSIS 16 adapter projects package metadata and collection counts without exposing the package path or native object.
- The `PackageOverviewInspector` passed a manual x64 smoke test against the user's WebProd Integration `Package.dtsx`: 9 connections, 25 variables, 2 executables, 1 precedence constraint, and no package execution.
- Read-only connection and variable inspectors are implemented. Their DTOs omit connection strings, variable values, and expression text; the SSIS 16 package exposes no variable sensitivity flag, so values are omitted unconditionally.
- The connection and variable inspectors passed a fresh-process smoke test against WebProd Integration `Package.dtsx`: 9 connections and 25 variables, matching native collection counts; all omission assertions passed and the package was not executed.
- A recursive executable inspector is implemented using SSIS metadata interfaces. It preserves native identity, creation name, description, hierarchy, container status, and expression-presence metadata, including for unknown task types.
- The executable inspector passed a fresh-process smoke test against WebProd Integration `Package.dtsx`: all 21 native executables were represented, with 2 roots and 2 sequence containers; no parent or creation-name metadata was missing. The package was not executed.
- `InspectionResult<T>` exposes completeness and unsupported items; task, SQL, expression, and data-flow projections use it to make partial coverage explicit.
- Parameter, SQL task, expression-presence, and data-flow inspectors are implemented. Parameters and variables omit values; SQL comments/literals and encrypted data-flow settings are redacted. Data-flow projections include column lineage/type/mapping metadata, runtime connection references, and an allowlist of source/destination settings; remaining custom properties are reported unsupported.
- `tests/Run-SsisAiRuntime.Ssis16SmokeTest.ps1` builds to an isolated temporary directory and checks a caller-supplied package in a fresh process. It passed against WebProd Integration: 9 connections, 25 variables, 0 parameters, 21 executables, 9 SQL tasks, 9 data flows, 18 components, 9 paths, 18 runtime connections, 122 input columns, 284 output columns, 246 external metadata columns, and 180 settings (1 redacted). It verifies counts/redaction without executing the package.
- Runtime-neutral focused context builders produce separate SQL, lineage, and configuration contexts from safe projections and propagate unsupported coverage.
- Session-scoped hierarchical semantic handles and a resolver are implemented. Handles use escaped names/hierarchy/ordinals rather than native IDs; duplicate-name lookups return candidate lists with `Ambiguous` status.
- A precedence inspector and handle-backed control-flow graph are implemented with containment and precedence edges. Unresolved endpoints are reported unsupported rather than guessed.
- The solution builds without an assembly-path override, and all 24 portable tests pass. A WebProd smoke produced 762 unique handles; all 114 duplicate-name groups resolved as ambiguous; the control-flow graph contained 21 nodes and 29 edges (19 containment, 10 precedence) with no unsupported endpoints. Data-flow lineage, dependency/search graph services, CLI, and AI skill remain.

## Next Actions

1. Add data-flow lineage queries over the projected column lineage IDs and paths.
2. Add predecessor/successor, dependency, and package-search services over the handle-backed graphs.
3. Expand the Windows smoke corpus and automate it in CI where SSIS 16 is available.

## Phase 1: Runtime Foundation

1. Create `SsisAiRuntime.sln`, `SsisAiRuntime.Core`, an SSIS 16 adapter, and Core tests. Keep Core independent of versioned Microsoft SSIS assemblies. Confirm target framework compatibility and SSIS 16 assembly resolution on Windows.
2. Define `PackageSession`, `IPackageLoader`, `PackageLoadResult`, and `RuntimeDiagnostics`. Keep the native runtime `Package` authoritative and available to runtime operations; session identity/path and projections must not become a parallel serialization model. Load through `Application.LoadPackage` and report failures explicitly.
3. Include environment facts determinable at load time: runtime assembly version, process architecture, package format/protection information when exposed, load warnings, and missing-component errors. Do not claim a package is editable merely because it loads or its XML is readable.
4. Test Core with fakes on macOS and run a real package-load smoke test on Windows with SSIS 16. The Windows build and package-load smoke test have passed, and the project resolves the known Binn/GAC layouts without a machine-specific override. Automate repeatable Windows package-load validation when the test corpus is established. The legacy UI/solution cleanup and README replacement are complete.

## Phase 2: Read-Only Inspectors

1. Add `SsisAiRuntime.Inspectors` for package overview, connections, variables, tasks, data flows, SQL, parameters, and expressions. Return small immutable records/projections, never live native/COM graphs as AI context. Package, connection, variable, parameter, SQL, expression-presence, recursive executable, and data-flow column/topology projections are implemented; deepen component-specific details incrementally.
2. Add focused context builders for `sql`, `lineage`, and `configuration` that include only relevant statements, mappings, expressions, and connection references. Runtime-neutral builders and tests are implemented; semantic handles are now available to enrich them.
3. Add explicit completeness and unsupported-component reporting. `InspectionResult<T>` and unsupported entries are implemented for executable, SQL, expression, and data-flow inspection. Preserve available identity, creation name/class ID, inputs/outputs, and graph position for unknown components rather than silently omitting them.
4. Redact passwords and other secrets in returned connection/configuration projections and logs; indicate sensitive values exist without returning their contents. Connection strings, parameter/variable values, expression text, SQL literals/comments, and encrypted custom settings are omitted or sanitized; extend redaction tests as projections deepen.

## Phase 3: Handles and Graph Services

1. Add stable hierarchical semantic handles and a resolver for tasks, connections, variables, data-flow components, and related objects. Detect duplicate names, report ambiguity with multiple candidates, and keep native IDs in diagnostics rather than primary handles. Session-scoped handles and ambiguity-aware name resolution are implemented; extend coverage as new object types are inspected.
2. Build control-flow, data-flow, precedence, lineage, and dependency query services: predecessors/successors, execution order/parallelism, column tracing, references, and package search. Return compact nodes/edges and diagnostics. Control-flow containment and precedence graphs are implemented; data-flow lineage and remaining graph queries are open.

## Phase 4: Deterministic CLI

1. Add `SsisAiRuntime.Cli` over the same application services, with commands for overview, connections, tasks, SQL, data-flow details, lineage, search, environment diagnosis, and focused `sql`/`lineage`/`configuration` context. Commands may ship incrementally. Keep output structured and stable for scripts and agents; do not put SSIS parsing logic in command handlers.
2. Test command behavior, exit codes, machine-readable output, diagnostics, and secret redaction. Validate on Windows with SSIS 16 and representative packages.

## Phase 5: Read-Only AI Skill

1. Add a skill/instruction layer that selects the appropriate CLI operation, explains safety and validation behavior, and presents results without reading or editing DTSX XML directly. Keep the initial skill strictly read-only.
2. Use a consistent result envelope containing operation, session/package identity, result, warnings, unsupported items, and redaction status. Keep MCP as a later wrapper over proven services rather than duplicating runtime logic.

## Phase 6: Narrow Mutations

1. Add explicit allowlisted operations rather than generic `set_property`: begin with variable values, parameter defaults, and task rename, then connection properties and SQL replacement. For each, resolve the handle, read current state, validate type/rules, mutate the native object, and report before/after without echoing secrets.
2. Test invalid targets/values, duplicate-handle ambiguity, expression overrides, metadata refresh requirements, and failure behavior. Keep original packages untouched by default.

## Phase 7: Save, Reload, Validate, Diff

1. Save mutations through native SSIS serialization to an explicit new destination. Refuse overwrite unless explicitly requested; distinguish an in-memory mutation from a saved result.
2. Reload the saved package, run available validation, compare intended versus actual semantic changes, record a modification journal, and return save/reload/validation/diff status. A mutation is not reported complete unless reload and required checks succeed; surface unexpected changes and warnings.

## Relevant Files

- `src/SsisAiRuntime.Core` — runtime-neutral sessions, loader contract, results, and diagnostics.
- `src/SsisAiRuntime.Ssis16` — SSIS 16 `Application.LoadPackage` adapter and native runtime/pipeline assembly references.
- `src/SsisAiRuntime.Inspectors` — runtime-neutral immutable projection contracts and result types.
- `src/SsisAiRuntime.Inspectors/SemanticHandleCatalogBuilder.cs` — session-scoped handle catalog and ambiguity-aware resolver.
- `src/SsisAiRuntime.Inspectors/ControlFlowGraphBuilder.cs` — handle-backed containment and precedence graph builder.
- `tests/SsisAiRuntime.Tests` — portable Core tests.
- `tests/Run-SsisAiRuntime.Ssis16SmokeTest.ps1` — repeatable Windows package-load and inspector smoke check.
- `SsisAiRuntime.sln` and `README.md` — sole solution entry point and headless setup documentation.
- `Plan.md` — implementation status, decisions, and sequencing for follow-on work.

## Verification

1. Core and inspector tests/build run without SSIS assemblies; Windows integration tests load representative packages with SSIS 16. The current Windows solution build, all 24 portable tests, and the repeatable smoke script against WebProd `Package.dtsx` pass, including column metadata, runtime connection links, data-flow settings, focused contexts, semantic handles, and control-flow graph edges. CI automation remains to be configured on an SSIS-enabled Windows host.
2. Inspector/context tests cover completeness, unknown components, redaction, compact projections, and focused `sql`/`lineage`/`configuration` output; graph tests cover ambiguous handles and expected relationships.
3. CLI tests verify structured output, deterministic diagnostics, exit codes, and no secret leakage. Skill checks confirm it invokes CLI operations and remains read-only.
4. Mutation/save integration checks use copies of representative packages: validation failure leaves originals untouched; successful saves reload; semantic diff contains intended changes and flags unexpected ones.
5. Confirm no WinForms/designer/resource files or legacy solution references remain after cleanup. SSIS 15 adapter validation is a separate future compatibility gate; v15/v16 side-by-side loading is not assumed.

## Decisions

- Native SSIS runtime owns authoritative loading, mutation, saving, and validation; XML is supplemental read-only evidence only. Do not implement normal-path DTSX XML mutation.
- SSIS 16 first; keep Core version-neutral and add SSIS 15 through a separate adapter/reference set. Do not promise side-by-side runtime loading without isolation tests.
- Forms, the legacy Explorer project, and the old solution were removed at the user's request before Windows runtime validation; preserve the existing `LICENSE`.
- Use read-only extraction first, CLI before the AI skill/protocol, and mutations last. The recommended initial AI integration is a skill wrapping CLI services; MCP can follow without embedding SSIS logic in the protocol layer.
- Stable semantic handles are session-scoped; ambiguous names must return candidates, never guess.
- Redact sensitive values by default, including logs. Mutation inputs may carry secrets, but outputs must not disclose them.
- Save-as to a new path by default, require explicit overwrite consent, then reload, validate, diff, and journal.

## Further Considerations

1. Confirm Core/CLI target frameworks and adapter bitness/assembly resolution on the Windows SSIS 16 host. Core is portable; the Windows adapter build and x64 package-load smoke test have been verified on this host. The project defaults to the standard Binn location, falls back to the v16 GAC path, and accepts `SSIS16ManagedDtsPath` for other layouts.
2. Set a representative package corpus and permitted credentials/components for Windows integration tests; do not commit secrets or unredacted connection strings.
3. Define the precise first-release skill host/format and CLI output schema before Phase 5; recommended default is a read-only agent skill invoking the CLI, with MCP deferred.
