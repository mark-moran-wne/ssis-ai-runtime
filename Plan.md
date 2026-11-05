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
- Mutation contracts and AI CLI requests now use exact schema `2.0`: strict operation-specific typed payloads, typed task/path targets, required source-hash matching, host-selected adapter/runtime identity, and a common lifecycle result. All `1.x` mutation requests are rejected; Corpus, inspection CLI, and FlowRunner versions remain independent.
- The November 5 native implementation is the current prototype checkpoint. Exact-path insertion, including selected Union All input insertion through a specialized metadata adapter, passes clean offline validation before mutation, after mutation, and after native save/reload. Existing inputs, mappings, outputs, identities, layout, and unrelated paths are preserved. Windows SSIS 16 CI enforcement is the next priority; package/project creation, SSISDB deployment, SQL Agent management, and SSIS 15 remain future work.


## Next Actions
1. Run `powershell.exe -NoProfile -File .\eng\ci\Invoke-Ssis16MutationGate.ps1` as the required `SSIS16-Mutation-Gate` Windows x64 SSIS 16 job in the repository's existing CI provider. It runs the Release solution build, portable suites, native integration/fixture harness, MutationHost lifecycle, designer/layout, column-resize, exact path-splice, and Union All clean-validation probes. The gate must fail on build/test/native load errors, save/reload failure, unexpected validation delta, semantic/layout mismatch, path/preservation failure, or rollback/restoration verification failure. Package execution probes stay outside the required gate. Configure the provider's PR check as required; branch protection is provider-managed.
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

1. Keep mutation and execution contracts separate. Bind previews to exact targets and source hashes; block only on coverage gaps that prevent correct application or verification.
2. Use the adapter boundary: mutation intent -> exact target/path -> topology splice -> component-specific adapter -> save/reload/validate and semantic/layout verification. `DataFlowPathSplicer` changes topology only; adapters own native metadata initialization, mapping preservation, `ReinitializeMetaData` when appropriate, and component verification. Report the selected adapter kind/version; discoverability never implies generic writability.
3. Expand typed edits in order: variables (metadata, expression, value), parameters, connections (safe non-secret metadata), then component discovery/adapters. Keep values and secrets behind explicit redaction/trusted-input boundaries.
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
- `src/SsisAiRuntime.MutationHost` — typed mutation plans, adapters, layout, and host lifecycle.
- `tests/SsisAiRuntime.Tests`, `tests/SsisAiRuntime.Corpus.Tests`, and `tests/SsisAiRuntime.Mutations.Tests` — portable suites.
- `tests/SsisAiRuntime.Ssis16IntegrationTests` — native scratch-fixture and lifecycle probes.
- `eng/ci/Invoke-Ssis16MutationGate.ps1` — provider-neutral Windows x64 SSIS 16 gate runner.
- `SsisAiRuntime.sln`, `README.md`, and `BuildPackage.md` — solution, usage, and build/gate instructions.

## Verification
1. Run the portable Core, inspector, AI, and CLI contract suite without SSIS, then execute the provider-neutral Windows x64 SSIS 16 gate in [BuildPackage.md](BuildPackage.md). Record the tested commit, SSIS version/architecture, and stage outcomes in [HISTORY.md](HISTORY.md). Make the CI provider's gate job a required PR status; package execution probes remain opt-in.
2. Inspector/context tests cover completeness, unknown components, redaction, compact projections, and focused `sql`/`lineage`/`configuration` output; graph tests cover ambiguous handles and expected relationships.
3. CLI and AI tool tests verify structured output, deterministic diagnostics, exit codes, selector ambiguity, bounded graph/impact results, and no secret leakage. The personal skill invokes CLI operations and remains read-only.
4. Mutation/save integration checks use copies of representative packages. Source artifacts remain unchanged unless overwrite was explicitly requested. Validation or verification failure must not publish the candidate as successful; the operation report must distinguish rejection, successful restoration, unverified restoration, and restoration failure. Successful artifacts must reload and satisfy required validation and semantic/layout verification.

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
