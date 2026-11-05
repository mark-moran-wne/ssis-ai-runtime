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
- Mutation contracts and AI CLI requests now use exact schema `2.0`: strict operation-specific typed payloads, typed task/path/executable targets, required source-hash matching, host-selected adapter/runtime identity, and a common lifecycle result. The CLI includes typed `rename_task` preview/apply; its release status remains gated by the Near-Term Milestone. All `1.x` mutation requests are rejected; Corpus, inspection CLI, and FlowRunner versions remain independent.
- The November 5 native implementation is the current prototype checkpoint. Exact-path insertion, including selected Union All input insertion through a specialized metadata adapter, passes clean offline validation before mutation, after mutation, and after native save/reload. Existing inputs, mappings, outputs, identities, layout, and unrelated paths are preserved. The required gate now includes a read-only corpus baseline check over seven checked-in structural fixtures; adding representative package fixtures and configuring the combined gate as a required provider check remain open. Package/project creation, SSISDB deployment, SQL Agent management, and SSIS 15 remain future work.

## Near-Term Milestone

Make deterministic package analysis and trustworthy dependency relationships the release authority, then promote exactly one narrowly scoped mutation. Expose `rename_task` as the sole release-grade CLI mutation with a typed schema 2.0 request and the verified MutationHost lifecycle. Existing native mutation implementations beyond RenameTask remain experimental prototypes; do not remove them, broaden their surface, or describe them as release-supported until this milestone is complete.

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

The milestone is complete when the corpus gate and graph acceptance matrix pass, repeated analysis is stable, and RenameTask passes lifecycle/invariant checks on representative packages. The target outcome is deterministic package analysis, explainable dependency/impact relationships, and one safely previewed, applied, reloaded, and verified native mutation without unintended semantic changes.


## Follow-On Actions (After Near-Term Milestone)
1. Run `powershell.exe -NoProfile -File .\eng\ci\Invoke-Ssis16MutationGate.ps1` as the required `SSIS16-Mutation-Gate` Windows x64 SSIS 16 job in the repository's existing CI provider. It runs the Release solution build, portable suites, structural corpus snapshot repeatability and approved-baseline verification, native integration/fixture harness, MutationHost lifecycle, designer/layout, column-resize, exact path-splice, and Union All clean-validation probes. The gate must fail on build/test/native load errors, corpus drift, save/reload failure, unexpected validation delta, semantic/layout mismatch, path/preservation failure, or rollback/restoration verification failure. Package execution probes stay outside the required gate. Configure the provider's PR check as required; branch protection is provider-managed.
2. [Complete] Define the schema 2.0 mutation envelope and migrate the AI CLI. Requests carry `schemaVersion`, `operation`, typed `target`, typed operation request, and source hash; the host derives requirements and adapter selection. Results share runtime/adapter identity, preview, lifecycle stages, diagnostics, coverage, and redaction. Existing-artifact hashes are checked against the file; all 1.x mutation requests are rejected.
3. Expand the existing `mutation.capabilities` surface to report installed runtime and adapter availability, target types, supported TaskHost property classes, preview/apply support, validation requirements, and limitations. It already describes the schema 2.0 envelope, target kinds, and typed request fields. Do not create a parallel capability registry.
4. Add variable discovery and separate `set_variable_metadata`, `set_variable_expression`, and `set_variable_value` operations. Values remain sensitive and redacted; never collapse these operations into a generic setter.
5. Add parameter discovery and typed mutation with sensitivity as a first-class property. Sensitive values may be reasoned about for existence/configuration but must never appear in inspection output.
6. Add `connection.properties`, `set_connection_metadata`, and typed non-secret `set_connection_property` only for explicitly classified safe fields. Never expose generic connection-string mutation. A future `set_connection_secret` requires a trusted input channel and must not serialize secrets into previews or results.
7. Add component discovery and typed adapters for identity, class/creation name, inputs, outputs, custom properties, runtime connections, column metadata, and known mutation support. Discoverability does not imply generic writability.
8. Add simple precedence constraints first: Success, Failure, and Completion add/remove operations. Verify the exact control-flow graph delta and preservation of unrelated constraints.
9. Add expression-bearing precedence constraints only after simple constraints. Verify both the expected control-flow graph delta and the expected dependency-graph delta.
10. Add task creation using a small discovered set of installed native task types and the common mutation lifecycle.
11. Add task removal with dependency/impact preview and explicit handling for constraints, expressions, variables, event handlers, containment, and designer metadata.
12. Implement `create_package` only after task, simple-constraint, variable, parameter, connection creation/configuration, basic Data Flow insertion/path connection, and layout primitives can construct a minimally useful package. Reuse those operations; do not add a parallel generation engine.
13. Implement `create_project` as composition over the same editor and mutation lifecycle.
14. Add SSIS Catalog (SSISDB) deployment as an independent capability.
15. Add SQL Server Agent job management as an independent capability.
16. Continue SSIS 15 compatibility as an isolated runtime-adapter track.

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

The following items are post-milestone directions, not authorization to expand the current release-supported mutation contract. Start them only after the corpus, graph-correctness, and RenameTask gates pass.

1. Keep mutation and execution contracts separate. Bind previews to exact targets and source hashes; block only on coverage gaps that prevent correct application or verification.
2. Use the adapter boundary: mutation intent -> exact target/path -> topology splice -> component-specific adapter -> save/reload/validate and semantic/layout verification. `DataFlowPathSplicer` changes topology only; adapters own native metadata initialization, mapping preservation, `ReinitializeMetaData` when appropriate, and component verification. Report the selected adapter kind/version; discoverability never implies generic writability.
3. Do not expand release-supported mutation scope until the Near-Term Milestone is complete. Existing variable, parameter, connection, component, and other mutation proposals remain deferred; keep secrets behind explicit redaction/trusted-input boundaries.
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
