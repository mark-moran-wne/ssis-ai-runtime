# Plan: Headless SSIS AI Runtime

Evolve the repository into a headless AI tool backed by the native SSIS runtime. SSIS 16 is the first supported runtime; SSIS 15 should be addable as a separate adapter. Build deterministic application services first and expose them through the CLI and AI-facing tools. Inspection is the initial capability; expose mutation tools as their native lifecycle and verification support becomes dependable. Completed milestones and investigation results are recorded in [HISTORY.md](HISTORY.md).

## Current Status

- Runtime foundation and read-only inspectors are implemented over the native SSIS 16 package model, with immutable redacted projections and explicit coverage reporting.
- Handles, control-flow, column lineage, metadata search, dependency queries, selector resolution, and classified impact are implemented.
- The CLI and deterministic AI snapshot services are implemented; they do not execute packages or call an LLM.
- Parsed SQL dependencies use ScriptDom with connection-scoped schema objects and explicit parsed evidence. Table/view/procedure catalog classification remains deferred.
- The bounded `ai context` tool is metadata-only by default; sanitized SQL/expression syntax is collected and emitted only through an explicit opt-in. LLM interpretation cannot alter graph construction or upgrade evidence.
- Variable/parameter discovery uses ANTLR lexical candidates and native-metadata scope resolution. Its edges are explicitly heuristic; native column observation remains separate and authoritative for column lineage.
- Expression grammar and owner coverage are bounded. Arbitrary custom-component contracts and automatic project-metadata discovery remain unsupported.
- Repository-registered skills, CI on an SSIS-enabled host, SSIS 15 compatibility, and broader AI-facing editing remain follow-on work. `ai mutation.capabilities`, `ai mutation.preview`, and `ai mutation.apply` expose current Derived Column/Data Conversion insertion operations. A native Flat File fixture verifies source validation and successful offline CLI apply through checkpoint, save/reload, and semantic verification. General package authoring, SSISDB deployment, and SQL Agent management are not implemented.

Recorded verification totals and native-binding evidence are in [HISTORY.md](HISTORY.md).

## Next Actions

1. Expand the AI-facing mutation workflow beyond the initial insertion operations. The model should select and sequence available operations to fulfill the user's request; previews and diagnostics guide execution rather than add redundant approval gates.
2. Expand existing-package editing to common task/property/expression, variable/parameter, connection-manager, control-flow, and data-flow changes. Provide discoverable operation/component metadata and operation-specific verification instead of a fixed small package-shape ceiling.
3. Add native package authoring so the model can turn job requirements into new SSIS packages or projects, build them through the SSIS object model, and verify save/reload results.
4. Add SSIS Catalog (SSISDB) deployment as a distinct capability for folders, projects, environments, references, and parameter configuration.
5. Add SQL Server Agent job management separately from deployment: create or update jobs, SSIS package steps, schedules, server assignment, retries, and logging.
6. Broaden native acceptance tests and CI. Keep all database-backed tests on Test DB; use native scratch fixtures for package-only behavior and explicit target selection for requested deployments. The successful CLI apply acceptance fixture now covers offline validation and publication without a database connection.
7. Expand the Windows SSIS 16 smoke corpus and automate it in CI on an SSIS-enabled host. Continue bounded expression/SQL coverage work and SSIS 15 compatibility as separate follow-on tracks.

## Phase 1: Runtime Foundation

1. Create `SsisAiRuntime.sln`, `SsisAiRuntime.Core`, an SSIS 16 adapter, and Core tests. Keep Core independent of versioned Microsoft SSIS assemblies. Confirm target framework compatibility and SSIS 16 assembly resolution on Windows.
2. Define `PackageSession`, `IPackageLoader`, `PackageLoadResult`, and `RuntimeDiagnostics`. Keep the native runtime `Package` authoritative and available to runtime operations; session identity/path and projections must not become a parallel serialization model. Load through `Application.LoadPackage` and report failures explicitly.
3. Include environment facts determinable at load time: runtime assembly version, process architecture, package format/protection information when exposed, load warnings, and missing-component errors. Do not claim a package is editable merely because it loads or its XML is readable.
4. Keep portable Core tests independent of SSIS and automate repeatable Windows package-load validation when the test corpus is established. Preserve standard Binn/GAC resolution and explicit path overrides for non-standard installations.

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
7. Keep raw expression text internal and short-lived in expression-owner inputs, never in inspection results, JSON, diagnostics, or AI context. Identify variable-expression owners by their owning container; explicitly project task/container, loop, precedence-constraint, and connection owner scopes. The analyzer invokes no resolver on parse failure; constants produce complete analyses with zero resolutions. Any unresolved candidate makes the analysis incomplete. Dependency results contain neither expression text nor raw parser diagnostics; separately generated sanitized snippets require explicit context opt-in and never become dependency evidence.
8. Only uniquely resolved, projected targets produce edges. Preserve existing `UsesVariable`/`UsesParameter` kinds unless downstream requirements justify a change, with consumer owner as `From` and dependency symbol as `To`. Keep variable and parameter identities independently selectable; deduplicate identical owner-target edges if needed while retaining positioned occurrences in parser results. Add explicit heuristic provenance (`LexicalAndScopeResolved`), never `Native`, `Verified`, or `NativeParser`; native target identity does not prove native binding.
9. Record redacted coverage codes: `expression.parse_failed`, `expression.owner_scope_missing`, `expression.scope_cycle`, `expression.reference_ambiguous`, `expression.reference_not_found`, `expression.project_context_unavailable`, and `expression.target_not_projected`. Coverage may identify an owner and allowlisted property category, but must not disclose expression/reference text, variable/parameter values, native exception messages, or ANTLR messages. Unresolved references never create guessed edges.

### Expression Dependency Acceptance Tests

Retain these gates as grammar and owner coverage expand. Completed fixture results and syntax observations are in [HISTORY.md](HISTORY.md#native-binding-investigation).

1. Parser: wrapped/unwrapped variables, system variables, package/project parameters, literals containing reference-like text, casts, functions, conditionals, repeated positioned references, constants, malformed syntax with no provisional references, and length limits.
2. Catalog/resolver: package/task owners, task and nested-container shadowing, parent fallback, same-scope duplicates, missing parents, cycles, missing symbols, namespace isolation, ambiguous unwrapped names, package-only parameter lookup, available/unavailable project context, and explicitly projected system variables. Catalog validation rejects invalid topology; defensive resolver handling reports unsupported topology rather than guessing.
3. Analyzer: parse failure never invokes resolution; constants are complete with no resolutions; one unresolved candidate makes analysis incomplete; output contains no expression text or raw diagnostics; repeated occurrences retain source positions.
4. Graph/impact: unique resolution emits a heuristic edge only to the nearest declaration; ambiguous/missing references emit no edges and report coverage; repeated owner-target edges can collapse; variables and parameters remain independently selectable; impact traverses these edges; existing native column-expression edges and their evidence remain unchanged.

## Phase 4: Deterministic CLI

1. `SsisAiRuntime.Cli` is implemented over the native adapter and runtime-neutral services. It provides overview, SQL, lineage, configuration, `inspect` with optional `--include`, trace, control-flow, predecessor/successor, search, and deterministic `ai` routes. Output is structured and redacted; command handlers contain no DTSX parsing logic.
2. Portable and native tests cover command behavior, exit codes, machine-readable output, diagnostics, and secret redaction. Continue validating against a broader package corpus on Windows with SSIS 16.

## Phase 5: AI Tool Integration

1. Inspection, mutation capability discovery, placement preview, and MutationHost apply are exposed as structured CLI AI tools over deterministic services. Add broader change planning and operation coverage incrementally. Tool descriptions and results should help the model determine the next useful action, supported component operations, required inputs, and relevant diagnostics. The model should carry out clear user requests without a redundant confirmation step; ask when intent or a consequential choice remains ambiguous.
2. Use stable structured contracts with operation, session/package identity where appropriate, results, diagnostics, unsupported items, coverage, and redaction status. Report unsupported or incomplete work precisely so the model can adapt or explain the remaining limitation. Keep MCP as a wrapper over proven services rather than duplicating runtime logic.
3. Keep the existing personal inspection skill read-only until it is connected to the mutation tools; that is the current implementation boundary, not a policy for the eventual AI integration. A repository-registered Copilot skill is optional.

## Phase 6: Existing Package Changes

1. Define mutation and execution contracts separately from runtime loading. `RenameTask` preview requires an executable native ID and graph-based impact. Coverage assessment must distinguish unrelated inspection gaps from evidence that directly affects the requested change; report actionable gaps and block only when the operation cannot be applied or verified correctly. The native MutationHost has scratch lifecycle tests for rename and bounded column resizing using injected doubles; file checkpointing and offline native package validation are implemented, while authorization and journaling remain deferred.
2. Provide typed, extensible operations rather than an opaque generic `set_property` interface or a fixed small allowlist. Make capabilities discoverable from installed component metadata and operation adapters. Cover common task settings/expressions, variables and parameters, connection managers, control-flow topology, and data-flow components/paths; retain the existing placement-aware insertion lifecycle and grow it with operation-specific preservation tests.
3. Read the current package state, apply requested changes through native SSIS APIs, save to a separate artifact by default, reload, validate, and compare intended versus observed changes. Honor explicit user requests to overwrite or perform other consequential actions; surface the exact target and effects. Preserve secrets in trusted inputs, never echo them in tool results.
4. Test supported operations against native scratch fixtures, including failure recovery, metadata refresh, preservation of unaffected objects, and clear reporting when a requested operation is unsupported by the installed runtime or component.

Execution plans now declare checkpoint, save-as, reload, validation, and semantic-diff requirements through immutable `MutationExecutionRequirements`. Rename previews attach all five obligations, serialized in mutation contract schema `1.1`; the column-resizing plan declares its corresponding lifecycle requirements. The MutationHost enforces these paths with injected services; file checkpointing and offline native package validation are implemented, while authorization and journaling remain deferred.

### Designer Layout Support (Prerequisite to Component Insertion)

Status: bounded layout extraction, placement, comparison, native `Package.DesignTimeProperties` writing, stable native-ID rebinding, and `DesignerPlacementPreviewer` are implemented in `SsisAiRuntime.MutationHost`. Control Flow positions round-trip in all seven fixtures. The SSDT-authored `TwoComponentHorizontal` fixture additionally round-trips both Data Flow component positions and a sampled EdgeLayout curve. `MutationHost` previews and executes constrained Derived Column and Data Conversion insertions through checkpoint/save-as/reload/validation/diff; versioned operation previews omit expression text and transient numeric IDs. A non-overwriting hash-verified file checkpoint service and offline native validator are implemented. The CLI exposes capability discovery, previews, and apply for both insertions, with native acceptance coverage for successful offline apply. Keep layout contracts separate from `Corpus`, `DependencyGraph`, and generic mutation contracts.

1. Introduce immutable `DesignerLayoutSnapshot`, `DesignerNode`, `DesignerConnection`, and `DesignerPlacement` contracts. A node contains exact native identity, owner scope where needed, and decimal left/top/width/height to preserve SSDT's fractional coordinates. A connection contains native endpoint identities and any persisted visual routing points that the SSIS model actually exposes. Canvas bounds are derived from known layout extents with a documented margin; do not assume a separate authoritative canvas rectangle exists.
2. Correlate executable nodes directly by native ID. Scope data-flow component identity by its owning data-flow task ID as well as the component native ID; a pipeline-local component ID alone is not assumed package-global. Never resolve placement targets by display name.
3. Implement `DesignerLayoutExtractor` at the native SSIS boundary. It traverses existing package/data-flow designer metadata and returns the lightweight snapshot, without exposing live COM objects or semantic package properties. Keep the snapshot contract and traversal separate from corpus projection.
4. Implement `DesignerLayoutService` over snapshots to provide deterministic `PlaceRightOf`, `PlaceBelow`, `FindOpenSpace`, and explicit `Normalize` operations. Placement considers existing node bounds and connection routes. Missing or invalid coordinates must produce an explicit diagnostic or placement refusal, never silently place every new component at `(0,0)`. Do not move existing nodes except during an explicit normalization request.
5. Implement `DesignerLayoutComparer` for before/expected/after layout checks. It verifies requested placements, preservation of existing node positions, and endpoint/routing consistency where routing metadata is available. It is independent of semantic verification and does not turn layout into package behavior evidence.
6. Keep designer layout out of `CorpusSnapshot`, `CorpusFingerprint`, `DependencyGraph`, lineage, semantic diffs, and generic `MutationContracts`. Moving a component without changing package semantics must leave corpus fingerprints and semantic/dependency diffs unchanged. Layout verification belongs to `DesignerLayoutComparer`. For insertion, define a separate placement intent such as right/below a stable native handle or deterministic open space. The model uses the user's request and package context to select an intent, not invent trusted pixel coordinates; `DesignerLayoutService` resolves coordinates and reports collisions or missing-layout constraints. Ask for clarification when available context cannot disambiguate a consequential choice, and otherwise make the requested change.
7. Component-insertion lifecycle: the AI inspects the package and user request, selects a placement intent, and uses the deterministic service to resolve exact coordinates. The preview communicates the planned change and binds it to the same pre-mutation layout snapshot used for execution. When the request is clear, the model can continue through the MutationHost apply operation without a redundant approval step. The host applies placement through native SSIS, inserts the component, and reconnects paths; save to a separate artifact, reload, run semantic verification, re-extract layout, and compare. A successful creation plan is incomplete unless placement is applied and verified. Do not edit DTSX XML or replace purpose-built tools with an opaque generic mutation interface.
8. Acceptance: the model can use inspection and placement tools to resolve ordinary insertion requests and complete them through save/reload. It requests clarification only when the target or intended outcome remains ambiguous, and clearly reports when package constraints prevent the requested operation. A representative saved insertion preserves all existing node positions and existing path endpoints/routes, persists the new component's placement through native save/reload, and leaves semantic fingerprints/diffs unchanged for layout-only movement.

`--create-designer-fixtures <directory>` writes seven unpositioned native SSIS topology fixtures and verifies native reload inventories. SSDT-authored Control Flow layouts pass for all seven; `TwoComponentHorizontal` also contains Data Flow component positions and a routed path. `--inspect-designer-fixtures <directory>` reports native owners/IDs, positions, endpoints, route counts, and fixed diagnostics. `--verify-designer-fixtures <directory>` saves each fixture to a separate temporary artifact, reloads it, compares extracted layout, and confirms the authored source remains unchanged. The focused probe tests decimal coordinates, TaskHost component layouts, cubic EdgeLayout sampling, deterministic placement, refusal behavior, and preservation comparisons. The native harness verifies direct Package.DesignTimeProperties writing and positioned Derived Column/Data Conversion insertion with route/layout preservation through save/reload. AI-facing capability discovery and both insertion previews are now callable through the CLI; apply wiring, production validation, and additional operation-specific capabilities and preservation tests remain. Source/destination insertion and path splitting are not yet implemented.

## Phase 7: New Package Generation

1. Convert a user request into a structured package/project design containing control-flow tasks, precedence constraints, data-flow components/paths, connection managers, variables, parameters, and relevant expressions. Use tools to inspect available components, required metadata, and the target SSIS version; do not make generation depend on a small set of hardcoded templates.
2. Build the design using native SSIS APIs and installed component contracts. Support package output and project deployment artifacts as appropriate, parameterize environment-specific values, and preserve secrets outside generated artifacts and model-visible results.
3. Save, reload, validate, and compare the generated artifact with the requested design. Report unsupported requirements and native diagnostics with enough detail for the model to revise the design and try again.

## Phase 8: SSISDB Deployment and SQL Agent Jobs

1. Treat SSIS Catalog deployment and SQL Server Agent job management as distinct operations. A deployed SSIS project/package is not automatically a scheduled Agent job; provide separate tools and status for each step.
2. Deploy or update projects in SSISDB, including folder/project identity, environments, environment references, and parameter bindings. Inspect the target and current registration first; make repeated requests idempotent and return the deployed version and catalog location.
3. Create or update SQL Agent jobs that invoke the deployed SSIS package, including job step type/options, catalog path, environment reference, schedules, retries, logging, and server assignment. Inspect existing job configuration and update the intended job rather than creating duplicates.
4. Use credentials supplied through trusted configuration/secret stores, never through model-visible tool output. Report permission and connection failures as actionable results. Database-backed tests target Test DB; production deployment occurs only when requested against an explicitly identified server/catalog.

## Phase 9: Save, Reload, Validate, Diff

1. Save mutations through native SSIS serialization to an explicit destination. Use a new path by default and honor an explicit overwrite request; distinguish an in-memory mutation from a saved result.
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

1. Run the full portable Core, inspector, AI, and CLI contract suite without loading SSIS packages, then run native scratch fixtures and actual CLI subprocess checks on an SSIS-enabled Windows host. Commands are in [BuildPackage.md](BuildPackage.md); record dated results in [HISTORY.md](HISTORY.md). CI automation remains to be configured.
2. Inspector/context tests cover completeness, unknown components, redaction, compact projections, and focused `sql`/`lineage`/`configuration` output; graph tests cover ambiguous handles and expected relationships.
3. CLI and AI tool tests verify structured output, deterministic diagnostics, exit codes, selector ambiguity, bounded graph/impact results, and no secret leakage. The personal skill invokes CLI operations and remains read-only.
4. Mutation/save integration checks use copies of representative packages: validation failure leaves originals untouched; successful saves reload; semantic diff contains intended changes and flags unexpected ones.
5. Confirm no WinForms/designer/resource files or legacy solution references remain after cleanup. SSIS 15 adapter validation is a separate future compatibility gate; v15/v16 side-by-side loading is not assumed.

## Decisions

- Native SSIS runtime owns authoritative loading, mutation, saving, and validation; XML is supplemental read-only evidence only. Do not implement normal-path DTSX XML mutation.
- SSIS 16 first; keep Core version-neutral and add SSIS 15 through a separate adapter/reference set. Do not promise side-by-side runtime loading without isolation tests.
- Keep the project headless and preserve the existing `LICENSE`; legacy desktop UI must not return through follow-on work.
- Use read-only extraction first, CLI before the AI skill/protocol, and mutations last. The recommended initial AI integration is a skill wrapping CLI services; MCP can follow without embedding SSIS logic in the protocol layer.
- Stable semantic handles are session-scoped; ambiguous names must return candidates, never guess.
- Redact sensitive values by default, including logs. Mutation inputs may carry secrets, but outputs must not disclose them.
- Save-as to a new path by default; honor a user's explicit overwrite request, then reload, validate, diff, and journal.
- Keep designer placement as a lightweight, native-ID-correlated projection separate from package semantics. Do not add layout fields to corpus fingerprints, dependency graphs, lineage, or semantic diffs.

## Further Considerations

1. Confirm adapter bitness and assembly resolution on additional Windows SSIS 16 installations; a successful check on one host is not a compatibility guarantee. Preserve portable Core and standard Binn/GAC resolution with full-path overrides.
2. Set a representative package corpus and permitted credentials/components for Windows integration tests; do not commit secrets or unredacted connection strings.
3. If a repository-local Copilot skill is desired, define its host/format and connect it to the AI tool surface as capabilities become available; MCP can remain a thin wrapper over the same services.
