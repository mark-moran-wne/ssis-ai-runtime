# Plan: Headless SSIS AI Runtime

Evolve the repository into a headless AI tool backed by the native SSIS runtime. SSIS 16 is the first supported runtime; SSIS 15 should be addable as a separate adapter. Build deterministic application services first, expose them through a CLI, then add a read-only AI skill. Defer mutation until inspection, diagnostics, and semantic diffs are dependable. The legacy WinForms project and solution have been removed at the user's request.

## Implementation Status (2026-10-05)

- Phase 1 foundation is implemented: `SsisAiRuntime.sln`, `netstandard2.0` Core, a `net48` SSIS 16 adapter, and `net10.0` Core tests.
- Core defines `PackageSession<TPackage>`, `IPackageLoader<TPackage>`, `PackageLoadResult<TPackage>`, and immutable runtime diagnostics. The native SSIS package remains available on the session.
- The SSIS 16 adapter uses `Application.LoadPackage` and avoids exposing exception messages or package paths in load-failure diagnostics.
- Windows Release solution build succeeds without a path override. The project resolves ManagedDTS and pipeline wrappers from `160\DTS\Binn` first, then their v16 GAC locations (including `DTSRuntimeWrap` in GAC_32), and supports explicit full-path overrides.
- A Windows smoke test loaded a local `.dtsx` package through `PackageLoader`: runtime assembly `16.0.0.0`, process architecture `x64`, zero diagnostics.
- Core tests pass on Windows: 4 passed, 0 failed. Earlier macOS Core test results were also 4 passed, 0 failed.
- On this Windows host, the v16 assembly is in the GAC, not in `160\DTS\Binn` or `160\SDK\Assemblies`. The project now has a GAC fallback and an explicit full-DLL-path override; both the default no-override build and the earlier explicit-path build succeeded.
- The native package API exposes overview candidates including `Name`, `ID`, `Description`, `CreationDate`, creator metadata, version fields, `ProtectionLevel`, `PackageType`, `Connections`, `Variables`, `Executables`, `PrecedenceConstraints`, and `Parameters`.
- Phase 2 read-only inspectors are implemented: `SsisAiRuntime.Inspectors` defines runtime-neutral immutable projections and completeness/unsupported reporting; the SSIS 16 adapter projects package metadata without exposing package paths or live native objects.
- The `PackageOverviewInspector` passed a manual x64 smoke test against the user's WebProd Integration `Package.dtsx`: 9 connections, 25 variables, 2 executables, 1 precedence constraint, and no package execution.
- Read-only connection and variable inspectors are implemented. Their DTOs omit connection strings, variable values, and expression text; the SSIS 16 package exposes no variable sensitivity flag, so values are omitted unconditionally.
- The connection and variable inspectors passed a fresh-process smoke test against WebProd Integration `Package.dtsx`: 9 connections and 25 variables, matching native collection counts; all omission assertions passed and the package was not executed.
- A recursive executable inspector is implemented using SSIS metadata interfaces. It preserves native identity, creation name, description, hierarchy, container status, and expression-presence metadata, including for unknown task types.
- The executable inspector passed a fresh-process smoke test against WebProd Integration `Package.dtsx`: all 21 native executables were represented, with 2 roots and 2 sequence containers; no parent or creation-name metadata was missing. The package was not executed.
- `InspectionResult<T>` exposes completeness and unsupported items; task, SQL, expression, and data-flow projections use it to make partial coverage explicit.
- Parameter, SQL task, expression-presence, and data-flow inspectors are implemented. Parameters and variables omit values; SQL comments/literals and encrypted data-flow settings are redacted. Data-flow projections include column lineage/type/mapping metadata, runtime connection references, and an allowlist of source/destination settings; remaining custom properties are reported unsupported.
- Earlier WebProd smoke checks passed with 9 connections, 25 variables, 0 parameters, 21 executables, 9 SQL tasks, 9 data flows, 18 components, 9 paths, 18 runtime connections, 122 input columns, 284 output columns, 246 external metadata columns, and 180 settings (1 redacted). Repeatable package checks now live in the native C# integration executable, with no script wrapper or package execution.
- Runtime-neutral focused context builders produce separate SQL, lineage, and configuration contexts from safe projections and propagate unsupported coverage.
- Session-scoped hierarchical semantic handles and a resolver are implemented. Handles use escaped names/hierarchy/ordinals rather than native IDs; duplicate-name lookups return candidate lists with `Ambiguous` status.
- A precedence inspector and handle-backed control-flow graph are implemented with containment and precedence edges. Unresolved endpoints are reported unsupported rather than guessed.
- The native integration build succeeds without an assembly-path override, and the current portable suite passes 143 tests. The Windows SSIS 16 native verifier passes scratch Data Conversion/Derived Column and heuristic expression-scope fixtures with CLI checks. Data-flow lineage, control-flow, metadata search, dependency graphs/queries, selector resolution, classified impact, and the read-only CLI are implemented. `SsisAiRuntime.AI` provides deterministic read-only tools and snapshot-based analysis; it does not call an LLM. The personal package-inspection skill is user-level and is not registered in this repository.
- Native SSIS 16 expression binding investigation is complete. `IDTSExpressionEvaluatorEx100.Parse` validates expressions and can expose column references through column observers, but it does not expose variable or parameter bindings. `Package.FindReferencedObjects` is present in the API but throws `NotImplementedException` on this runtime. Continue using the native parser for column lineage; add an ANTLR-based expression reference parser for variable and parameter dependency discovery, resolve references against package/container scope using executable hierarchy metadata, and mark resulting dependency edges as heuristic rather than native. The test-only probe does not evaluate or modify the package.

## Next Actions

1. Expand the bounded ANTLR grammar and expression-owner corpus using native fixtures; add project-backed metadata discovery only when explicitly scoped. The parser/resolver/heuristic graph pipeline is implemented.
2. Expand the Windows SSIS 16 smoke corpus and automate it in CI on an SSIS-enabled host.
3. Decide whether to add a repository-registered Copilot skill; the existing inspection skill is user-level and invokes the read-only CLI.
4. Keep read-write operations deferred until their allowlist, save-as behavior, reload/validation, and semantic-diff contract are explicitly scoped and tested.

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
2. Build control-flow, data-flow, precedence, lineage, and dependency query services. Implemented services include predecessor/successor traversal, column tracing, metadata search, dependency graph construction, incoming/outgoing dependency queries, selector resolution, and classified impact. Existing native edges represent proven projected relationships; unresolved endpoints remain explicit coverage gaps. Variable/parameter reference discovery now uses the explicitly heuristic pipeline below. Execution simulation remains unsupported.

### Expression Dependency Implementation Boundary

Status: implemented with bounded grammar and native owner coverage. Native data-flow column observation remains authoritative and unchanged. ANTLR supplies only lexical variable/system-variable/package-parameter/project-parameter candidates; neither parsing nor resolution alone creates edges. The adapter covers package/task/container/connection property expressions, evaluated variable expressions, For Loop expressions, precedence constraints, event-handler scopes, and registered Derived Column expressions. Arbitrary custom-component expression contracts remain unsupported. Project parameters require explicitly supplied projected metadata; standalone CLI package loads have no project context.

Pipeline: native expression owner and scope ID -> ANTLR candidates -> scope resolver -> dependency projection -> graph enrichment.

1. Put runtime-neutral grammar, parser contracts, immutable scope model, resolver, and analysis results under `src/SsisAiRuntime.Inspectors/Expressions/`, targeting `netstandard2.0`. Keep native hierarchy and expression-owner projection in `SsisAiRuntime.Ssis16`. AI consumes the enriched graph only; CLI contains no expression parsing or scope logic.
2. The parser classifies reference syntax and records individual source positions. It parses syntax, ignores reference-like text in string literals, enforces a length limit, and returns no provisional references after any parse failure. Parsing has no symbol lookup, scope, native identity, resolution evidence, or edge responsibilities.
3. The native adapter projects immutable scopes for package, sequence, loops, tasks, and event handlers, with explicit parent IDs and symbols retaining native IDs, scope IDs, kinds, namespaces, and names. Do not pass live SSIS/COM objects to the resolver or infer owners from display names. Project only declarations belonging to each scope, not inherited variables as local declarations.
4. Validate unique scope IDs, exactly one package scope, existing parents, absence of cycles, symbol/containing-scope agreement, and native symbol ID uniqueness within each symbol class. Invalid catalogs become inspection coverage gaps; never repair topology by guessing. Model availability of explicitly supplied project metadata separately from an empty parameter inventory.
5. Resolve variables from the owner's native container toward the package. Match namespace/name case-insensitively unless native fixtures establish different behavior. Stop at the nearest scope containing any matches: one resolves, multiple are ambiguous, and parent matches cannot override either result. Unwrapped names match across namespaces conservatively and never silently default to `User`. System variables resolve only against explicitly projected system-variable symbols.
6. Resolve package parameters only against the package parameter inventory. Resolve project parameters only against explicitly supplied project metadata; unavailable project context is `Unsupported`, whereas a missing name in an explicitly complete inventory is `NotFound`. Preserve `Resolved`, `Ambiguous`, `NotFound`, `Unsupported`, and `InvalidOwnerScope` outcomes, with exactly one candidate required for `Resolved`.
7. Keep expression text internal and short-lived in expression-owner inputs, never in inspection results, JSON, diagnostics, or AI context. Identify variable-expression owners by their owning container; explicitly project task/container, loop, precedence-constraint, and connection owner scopes. The analyzer invokes no resolver on parse failure; constants produce complete analyses with zero resolutions. Any unresolved candidate makes the analysis incomplete. Results contain neither expression text nor raw parser diagnostics.
8. Only uniquely resolved, projected targets produce edges. Preserve existing `UsesVariable`/`UsesParameter` kinds unless downstream requirements justify a change, with consumer owner as `From` and dependency symbol as `To`. Keep variable and parameter identities independently selectable; deduplicate identical owner-target edges if needed while retaining positioned occurrences in parser results. Add explicit heuristic provenance (`LexicalAndScopeResolved`), never `Native`, `Verified`, or `NativeParser`; native target identity does not prove native binding.
9. Record redacted coverage codes: `expression.parse_failed`, `expression.owner_scope_missing`, `expression.scope_cycle`, `expression.reference_ambiguous`, `expression.reference_not_found`, `expression.project_context_unavailable`, and `expression.target_not_projected`. Coverage may identify an owner and allowlisted property category, but must not disclose expression/reference text, variable/parameter values, native exception messages, or ANTLR messages. Unresolved references never create guessed edges.

### Expression Dependency Acceptance Tests

Portable parser/resolver/analyzer/graph tests pass. Native save/reload fixtures prove declaration ownership, package/task/nested shadowing, loop assignments, event handlers, constraints, package parameters, Derived Column variable references, CLI evidence/redaction, failed-catalog coverage, and unchanged hashes. Project inventory resolution is tested with explicit projections, not a project-backed SSIS fixture.

1. Parser: wrapped/unwrapped variables, system variables, package/project parameters, literals containing reference-like text, casts, functions, conditionals, repeated positioned references, constants, malformed syntax with no provisional references, and length limits.
2. Catalog/resolver: package/task owners, task and nested-container shadowing, parent fallback, same-scope duplicates, missing parents, cycles, missing symbols, namespace isolation, ambiguous unwrapped names, package-only parameter lookup, available/unavailable project context, and explicitly projected system variables. Catalog validation rejects invalid topology; defensive resolver handling reports unsupported topology rather than guessing.
3. Analyzer: parse failure never invokes resolution; constants are complete with no resolutions; one unresolved candidate makes analysis incomplete; output contains no expression text or raw diagnostics; repeated occurrences retain source positions.
4. Graph/impact: unique resolution emits a heuristic edge only to the nearest declaration; ambiguous/missing references emit no edges and report coverage; repeated owner-target edges can collapse; variables and parameters remain independently selectable; impact traverses these edges; existing native column-expression edges and their evidence remain unchanged.

## Phase 4: Deterministic CLI

1. `SsisAiRuntime.Cli` is implemented over the native adapter and runtime-neutral services. It provides overview, SQL, lineage, configuration, `inspect` with optional `--include`, trace, control-flow, predecessor/successor, search, and deterministic `ai` routes. Output is structured and redacted; command handlers contain no DTSX parsing logic.
2. Portable and native tests cover command behavior, exit codes, machine-readable output, diagnostics, and secret redaction. Continue validating against a broader package corpus on Windows with SSIS 16.

## Phase 5: Read-Only AI Skill

1. A personal user-level inspection skill selects CLI operations and explains read-only safety. No repository-registered Copilot skill is included; adding one is optional follow-on work.
2. The CLI uses a stable JSON envelope with operation, session/package identity where appropriate, results, diagnostics, unsupported items, coverage, and redaction status. Keep MCP as a later wrapper over proven services rather than duplicating runtime logic.

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
- `tests/SsisAiRuntime.Ssis16IntegrationTests` — native C# scratch-fixture verification and optional read-only package smoke checks.
- `SsisAiRuntime.sln` and `README.md` — sole solution entry point and headless setup documentation.
- `Plan.md` — implementation status, decisions, and sequencing for follow-on work.

## Verification

1. Core, inspector, AI, and CLI contract tests build without loading SSIS packages; the current portable suite passes 112 tests. The native C# verifier passes scratch Data Conversion/Derived Column fixtures and actual CLI subprocess checks. CI automation remains to be configured on an SSIS-enabled Windows host.
2. Inspector/context tests cover completeness, unknown components, redaction, compact projections, and focused `sql`/`lineage`/`configuration` output; graph tests cover ambiguous handles and expected relationships.
3. CLI and AI tool tests verify structured output, deterministic diagnostics, exit codes, selector ambiguity, bounded graph/impact results, and no secret leakage. The personal skill invokes CLI operations and remains read-only.
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
3. If a repository-local Copilot skill is desired, define its host/format and keep it as a read-only client of the CLI; MCP remains deferred.
