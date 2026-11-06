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
- `ai variable.properties` now provides package-scope variable metadata with native IDs, expression-presence flags, and value redaction. Nested executable scopes are reported as incomplete; sensitivity remains unclassified. Typed description, expression, and environment-backed value mutation prototypes are implemented with redacted responses; value writes are not supported for sensitive/unclassified variables.
- Mutation contracts and AI CLI requests now use exact schema `2.0`: strict operation-specific typed payloads, typed task/path/executable/variable targets, required source-hash matching, host-selected adapter/runtime identity, and a common lifecycle result. The CLI includes typed `rename_task` and package-scope variable metadata/expression/value prototypes; release status remains per-operation and evidence-gated. All `1.x` mutation requests are rejected; Corpus, inspection CLI, and FlowRunner versions remain independent.
- The November 5 native implementation is the current prototype checkpoint. Exact-path insertion, including selected Union All input insertion through a specialized metadata adapter, passes clean offline validation before mutation, after mutation, and after native save/reload. Existing inputs, mappings, outputs, identities, layout, and unrelated paths are preserved. The required gate now includes a read-only corpus baseline check over seven checked-in structural fixtures; adding representative package fixtures and configuring the combined gate as a required provider check remain open. Package/project creation, SSISDB deployment, SQL Agent management, and SSIS 15 remain future work.

## Near-Term Milestone

Advance implementation and assurance in parallel. Keep the existing SSIS16 gate protecting current behavior while the programmer continues landing useful, typed feature slices. Use the corpus, graph, and RenameTask invariants to decide maturity and release support, not to decide whether adjacent prototype work may begin.

1. **Corpus regression gate:** The starter corpus is `tests/LayoutFixtures/*.dtsx`, with reviewed snapshots in `tests/LayoutFixtures/CorpusBaselines`. The required gate repeats snapshots and verifies each against its approved baseline without writing. Add representative, sanitized package fixtures when available; review every semantic delta before explicitly approving a baseline update. Inspector, graph, identity, dependency-extraction, and serialization changes must account for each delta.
2. **Determinism:** Repeated analysis of the same package must produce semantically identical snapshots and fingerprints. Normalize only known transient identifiers and serialization noise; retain meaningful identity, topology, metadata, evidence, coverage, and diagnostics. Verify ordering and canonicalization independently of package save/reload.
3. **Graph correctness:** Build focused native fixtures and exact edge assertions for task-to-connection-manager, task-to-variable, expression-to-variable, Execute SQL Task-to-database-object, Data Flow source/destination, precedence constraints, package/project parameters, Execute Package relationships when the native target is resolved, and indirect expression references. Record evidence and confidence per edge. Until Execute Package target metadata is modeled, emit `dependency.execute_package_target_unresolved`; a missing or ambiguous relationship must remain unresolved/covered, not guessed into existence.
4. **RenameTask as the only release-grade mutation:** The typed schema 2.0 CLI route now exists. Verify its complete lifecycle and explicit semantic invariants on representative packages before calling it stable. Until the corpus and graph gates also pass, it remains prototype scope; other native mutation operations stay experimental.
5. **Required CI checks:** `Invoke-Ssis16MutationGate.ps1` includes the corpus semantic-diff and repeatability checks. Configure its stable `SSIS16-Mutation-Gate` status as required in the existing Windows x64 SSIS 16 CI provider. Branch protection remains provider-managed.

```text
Load
-> Analyze Before
-> Validate Request
-> Preview Mutation
-> Apply Native Mutation
-> Save As New File
-> Reload
-> Analyze After
-> Verify Expected Delta
```

For RenameTask, the requested task name is the intended change. Allow only documented SSIS serialization metadata changes; forbid task-count, DTSID, connection-manager, executable-hierarchy, expression, SQL-dependency, and unrelated-property changes. Preserve the source artifact and report rollback only when restoration has actually been verified.

Defer generalized arbitrary-property editing, AI-driven automatic repair, package XML rewriting, broad refactoring operations, and an elaborate UI. Existing typed native mutation prototypes remain available for focused experiments but do not expand the release contract.

This milestone describes the target confidence for promoting RenameTask and the analysis surface; it is not a prerequisite for starting other typed prototype slices. The target outcome is deterministic package analysis, explainable dependency/impact relationships, and a safely previewed, applied, reloaded, and verified native mutation without unintended semantic changes.


## Work Tracks

### Build Next

Start with incremental variable support. Each slice should leave a useful, typed result; later slices do not need to block the first:

1. [Complete, package scope] Add read-only `variable.properties` discovery for names, native IDs, types, scope, expression presence, and sensitivity metadata. The current slice reports package-scope variables, marks sensitivity `Unclassified`, always omits values, and emits coverage for nested scopes not yet inspected.
2. [Prototype, Description only] `set_variable_metadata` binds to the package variable's native ID and source hash, saves to a separate file, and verifies the description after reload; previews/results omit the description values.
3. [Prototype, package scope] `set_variable_expression` replaces one package-scope expression, enables `EvaluateAsExpression`, preflights syntax and uniquely resolved references, validates offline natively, and verifies the dependency after reload. Text is omitted from preview/results.
4. [Prototype, process environment only] `set_variable_value` accepts an environment-variable name, never a direct JSON value. It refuses system, read-only, expression-owned, nested-scope, and unsupported-type targets. Sensitivity remains `Unclassified`; values never enter previews/results/logs, and this prototype is not a supported secret-writing path.
5. **Next:** Determine sensitivity through a trusted, verifiable metadata policy before allowing any sensitive-variable writes; add nested-scope discovery, parent/child shadowing, and failure coverage incrementally.

Prefer narrow operation-specific contracts over a universal property setter. Parameter discovery/edits, safe connection metadata, precedence constraints, component adapters, and task operations can be developed as subsequent independent slices. Preserve actual dependencies (for example, test simple constraints before expression-bearing constraints), but do not require the entire Near-Term Milestone before beginning them.

### Harden Alongside

1. Broaden `tests/LayoutFixtures/CorpusBaselines` with sanitized, representative packages as they become available. Every baseline update should show the semantic diff and receive review; tests and gates verify baselines but never approve them automatically.
2. Expand the graph acceptance matrix, including package/project parameters, indirect expression references, and Execute Package relationships when native target metadata can be resolved. Keep unresolved targets explicit in coverage.
3. Configure `SSIS16-Mutation-Gate` as a required PR status in the existing CI provider. Keep package execution opt-in.
4. Extend lifecycle, redaction, semantic-delta, layout, and rollback tests alongside each feature slice. A failure to restore must never be reported as a successful rollback.
5. Continue improving `mutation.capabilities` with runtime/adapter availability, target types, operation fields, lifecycle requirements, and limitations; do not create a parallel capability registry.

### Maturity and Promotion

Track maturity per operation, not per project. A prototype may be implemented and used for focused development before it is promoted:

1. `Prototype`: typed intent and a working slice; limitations are explicit.
2. `Native Fixture Verified`: exact native behavior and intended/unintended deltas are tested on scratch packages.
3. `Lifecycle Verified`: source binding, preview, checkpoint, separate save, reload, validation, semantic/layout checks, redaction, and failure/rollback outcomes are tested as applicable.
4. `Corpus Verified`: representative approved baselines cover the operation's affected package semantics, and every delta is explained.
5. `Required-CI Covered`: relevant portable/native/corpus checks run in the required provider gate.
6. `Supported`: the operation's declared contract and maturity evidence are documented. Only this label implies release support.

Promotion requires the maturity evidence for that operation. Missing representative packages or unrelated graph coverage are not stop-work blockers for implementing the next typed slice; actual data-loss risks, unsafe ambiguity, secret exposure, and architectural choices that cause major rework remain blockers for the affected operation.

## Phase 1: Runtime Foundation

SSIS 16 loading, version-neutral Core contracts, environment diagnostics, and x64 native assembly resolution are implemented. The native `Package` remains authoritative; loading alone does not establish editability. Preserve standard Binn/GAC resolution and keep portable tests SSIS-independent.

## Phase 2: Read-Only Inspectors

Overview, configuration, SQL, data-flow, lineage, and context projections are implemented as bounded metadata contracts with explicit coverage. Preserve unknown-component identity and topology; redact values, connection strings, SQL/expression text, and secrets by default.

## Phase 3: Handles and Graph Services

Session-scoped handles, ambiguity-aware resolution, control/data-flow graphs, lineage, search, dependency, and impact queries are implemented. Native column relationships remain authoritative; variable/parameter references remain explicitly heuristic. Execution simulation is unsupported.

### Expression Dependency Boundary

Native column observation remains authoritative. Variable and parameter references are lexical/scope-resolved heuristics and never upgrade to native evidence; unresolved or ambiguous references produce no guessed edges. Raw expressions and values remain redacted. Arbitrary custom-component contracts and automatic project-metadata discovery are unsupported. Detailed grammar, owner coverage, resolver rules, and acceptance results are recorded in [HISTORY.md](HISTORY.md#native-binding-investigation).

## Phase 4: Deterministic CLI

The CLI exposes structured, redacted inspection, graph, context, capability, preview, and apply routes over deterministic services. It contains no DTSX parsing or package execution logic; broader package-corpus coverage remains follow-up work.

## Phase 5: AI Tool Integration

AI-facing tools wrap the CLI's deterministic inspection, capability, preview, and apply services. Clear requests proceed without redundant confirmation; ambiguous consequential choices require clarification. Keep MCP and skills as thin clients, not parallel runtime implementations.

## Phase 6: Cross-Cutting Mutation Lifecycle Requirements

1. Use one versioned request envelope with `schemaVersion`, `operation`, typed `target`, typed `request`, and `sourceArtifactHash`. The host supplies `requirements` and the typed preview in its response; callers cannot select lifecycle requirements or adapters. Existing-artifact mutations require a source hash; creation may use null only when the typed operation contract explicitly sets `requirements.requiresExistingSource` to false. Keep operation payloads strongly typed; never use a universal setter or `Dictionary<string, object>`.
2. Standardize the result envelope across operations. Include `result.runtime` (`ssisMajorVersion`, architecture), `result.adapter` (`kind`, version, selected by the host), `result.mutation`, `result.artifact`, `result.reload`, `result.validation`, `result.semanticVerification`, `result.layoutVerification`, and `result.rollback`, with diagnostics, coverage, and redaction. Preview reports the same runtime and adapter identity; callers never select the adapter. An operation may mark a result category not applicable, but must not invent a separate lifecycle response shape. Layout-specific failure detail remains `Unverified` until MutationHost reports it independently.
3. Make rollback status explicit for every mutation. Distinguish `AppliedAndVerified`, `AppliedWithWarnings`, `RejectedBeforeMutation`, `FailedAndRestored`, `FailedAndRestorationUnverified`, and `FailedAndRestoreFailed` (or equivalent states). A failed operation does not imply restoration.
4. Require an operation report for every MutationHost invocation: operation, source hash, destination, checkpoint, requested delta, observed delta, validation, verification, rollback status, and diagnostics. Persistent authorization-aware audit journaling, identities, timestamps, and retention remain deferred.
5. Apply the native lifecycle as required by each operation: inspect, preview, bind the preview to the source hash, reverify that hash immediately before checkpointing or mutation, checkpoint, mutate, save to a separate artifact, reload, validate offline, compare intended and unintended semantic changes, and verify layout where applicable. A mismatch must return `RejectedBeforeMutation`; never regenerate the preview or mutate a changed source artifact. A mutation is not reported complete until its required checks succeed. Package execution probes remain separate and opt-in.

## Phase 7: Existing Package Changes

The following are implementation directions, not release-support claims. Prototype slices may be developed incrementally while corpus, graph-correctness, and RenameTask evidence matures; promote each operation only when its own relevant gates pass.

1. Keep mutation and execution contracts separate. Bind previews to exact targets and source hashes; block only on coverage gaps that prevent correct application or verification.
2. Use the adapter boundary: mutation intent -> exact target/path -> topology splice -> component-specific adapter -> save/reload/validate and semantic/layout verification. `DataFlowPathSplicer` changes topology only; adapters own native metadata initialization, mapping preservation, `ReinitializeMetaData` when appropriate, and component verification. Report the selected adapter kind/version; discoverability never implies generic writability.
3. Implement variable, parameter, connection, and component operations as narrow typed slices when useful. Keep each at its evidenced maturity level; do not promote it to supported until its own contract, lifecycle, corpus, and CI evidence is sufficient. Keep secrets behind explicit redaction/trusted-input boundaries.
4. Add simple Success, Failure, and Completion constraint edits first. Verify exact ControlFlowGraph delta and preserve unrelated nodes, constraints, task identities, containment, and layout.
5. Add expression-bearing constraints only after simple constraints. Verify both expected ControlFlowGraph and dependency-graph deltas.
6. Add task creation, then dependency-aware task removal. Use discovered native task types; preview removal impact and explicitly handle constraints, expressions, variables, event handlers, containment, and layout.
7. Test adapters and operations on native scratch fixtures. Cover metadata refresh, rollback states, save/reload, intended/unintended diffs, and preservation of unaffected objects. Historical fixture details and completed lifecycle results are in [HISTORY.md](HISTORY.md).


### Designer Layout Boundary

Native layout extraction, placement, comparison, and writing are implemented. Keep layout separate from package semantics and correlate Data Flow objects by TaskHost plus native identity. Refuse missing/ambiguous coordinates; never move existing nodes except under explicit normalization. Insertion previews bind the layout snapshot, and save/reload verification preserves existing positions/routes except the requested path replacement. Detailed fixtures and completed probes are in [HISTORY.md](HISTORY.md).

## Phase 8: New Package Generation

1. Implement `create_package` only after existing primitives can build a minimally useful package: task creation, simple constraints, variables, parameters, connection creation/configuration, basic Data Flow insertion/path connection, and layout. Construct it from those operations; do not create a parallel generation engine.
2. Add `create_project` later as a separate bootstrap operation composed over the same editor. Neither creation operation should invent a source artifact hash when no source exists; use the creation requirements defined in Phase 6.
3. Keep the requested design and native metadata explicit; verify the created artifact through the common save/reload/validation/semantic/layout lifecycle and report unsupported requirements precisely.

## Phase 9: SSISDB Deployment and SQL Agent Jobs

1. Treat SSIS Catalog deployment and SQL Server Agent job management as distinct operations. A deployed SSIS project/package is not automatically a scheduled Agent job; provide separate tools and status for each step.
2. Deploy or update projects in SSISDB, including folder/project identity, environments, environment references, and parameter bindings. Inspect the target and current registration first; make repeated requests idempotent and return the deployed version and catalog location.
3. Create or update SQL Agent jobs that invoke the deployed SSIS package, including job step type/options, catalog path, environment reference, schedules, retries, logging, and server assignment. Inspect existing job configuration and update the intended job rather than creating duplicates.
4. Use credentials supplied through trusted configuration/secret stores, never through model-visible tool output. Report permission and connection failures as actionable results. Database-backed tests target Test DB; production deployment occurs only when requested against an explicitly identified server/catalog.

## Relevant Files

- `src/SsisAiRuntime.Core` — runtime-neutral sessions, loader contract, results, and diagnostics.
- `src/SsisAiRuntime.Ssis16` — SSIS 16 loader and native runtime/pipeline assembly references.
- `src/SsisAiRuntime.Inspectors` — immutable projections, handles, and graph/query services.
- `src/SsisAiRuntime.Inspectors/SemanticHandleCatalogBuilder.cs` — session-scoped handles and ambiguity-aware resolution.
- `src/SsisAiRuntime.Inspectors/ControlFlowGraphBuilder.cs` — containment and precedence graph construction.
- `src/SsisAiRuntime.Corpus` — deterministic snapshots, fingerprints, semantic diffs, and approved-baseline storage for the corpus release gate.
- `eng/ci/Invoke-CorpusBaselineGate.ps1` and `tests/LayoutFixtures/CorpusBaselines` — read-only seed corpus repeatability and approved-baseline gate.
- `src/SsisAiRuntime.MutationHost` — typed mutation plans, adapters, layout, and host lifecycle.
- `tests/SsisAiRuntime.Tests`, `tests/SsisAiRuntime.Corpus.Tests`, and `tests/SsisAiRuntime.Mutations.Tests` — portable suites.
- `tests/SsisAiRuntime.Ssis16IntegrationTests` — native scratch-fixture and lifecycle probes.
- `eng/ci/Invoke-Ssis16MutationGate.ps1` — provider-neutral Windows x64 SSIS 16 gate runner.
- `SsisAiRuntime.sln`, `README.md`, and `BuildPackage.md` — solution, usage, and build/gate instructions.

## Verification
1. Run portable suites and the provider-neutral Windows x64 SSIS 16 gate in [BuildPackage.md](BuildPackage.md). Record the tested commit, runtime/architecture, and stage outcomes in [HISTORY.md](HISTORY.md); keep package execution probes opt-in.
2. Analyze each curated corpus fixture repeatedly; require semantically identical snapshots/fingerprints. Compare against approved baselines and require reviewed explanations for every semantic delta before approval.
3. Graph tests assert exact expected relationships and preserve evidence strength, including unresolved/ambiguous negative cases, across the Near-Term Milestone matrix.
4. CLI and AI tool tests verify structured output, deterministic diagnostics, exit codes, selector ambiguity, bounded graph/impact results, and no secret leakage. The personal skill invokes CLI operations and remains read-only.
5. RenameTask integration checks use source copies and assert the complete lifecycle plus intended/forbidden semantic deltas. The source remains unchanged; failure results distinguish rejection, verified restoration, unverified restoration, and restore failure.

## Decisions

1. Native SSIS runtime owns authoritative loading, mutation, saving, and validation; XML is supplemental read-only evidence only. Do not implement normal-path DTSX XML mutation.
2. SSIS 16 first; keep Core version-neutral and add SSIS 15 through a separate adapter/reference set. Do not promise side-by-side runtime loading without isolation tests.
3. Keep the project headless and preserve the existing `LICENSE`; legacy desktop UI must not return through follow-on work.
4. Use read-only extraction first, CLI before the AI skill/protocol, and mutations last. MCP remains a wrapper over proven services rather than duplicating runtime logic.
5. Stable semantic handles are session-scoped; ambiguous names must return candidates, never guess.
6. Redact sensitive values by default, including logs. Mutation inputs may carry secrets, but outputs must not disclose them.
7. Save-as to a new path by default; honor a user's explicit overwrite request, then reload, validate, and diff. Emit an operation report for every invocation; persistent authorization-aware audit journaling is deferred.
8. Keep designer placement as a lightweight, native-ID-correlated projection separate from package semantics. Do not add layout fields to corpus fingerprints, dependency graphs, lineage, or semantic diffs.

## Further Considerations

1. Confirm adapter bitness and assembly resolution on additional Windows SSIS 16 installations; a successful check on one host is not a compatibility guarantee. Preserve portable Core and standard Binn/GAC resolution with full-path overrides.
2. Set a representative package corpus and permitted credentials/components for Windows integration tests; do not commit secrets or unredacted connection strings.
