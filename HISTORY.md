# SSIS AI Runtime History

## 2026-11-05 Synthetic Semantic Corpus Fixtures

- Added `SyntheticScopedControlFlow.dtsx` for nested scope, shadowing, precedence, and expression references, plus `SyntheticSqlDependencies.dtsx` for connection-scoped table, procedure, and function edges. Both are generated through SSIS 16's object model, contain no production data, and do not execute or connect to a data source.
- Reviewed each metadata-only snapshot for intended nodes, evidence-bearing edges, explicit coverage gaps, deterministic repeated output, and absence of raw SQL/expression/connection fields; then explicitly approved the two baselines. `Invoke-CorpusBaselineGate.ps1` passes all nine fixtures.
- The full `SSIS16-Mutation-Gate` passes after the additions: Release solution build, 202 runtime tests, 13 corpus tests, 23 mutation tests, all nine corpus baselines, default native harness, 30 MutationHost lifecycle cases, designer/layout, 15 resize cases, exact-path splice, and Union All offline validation. The known `CS8012` interop warning remains; no package execution or data-source connection occurred.
- These synthetic packages expand semantic shape coverage only; they are not representative business packages, and no operation-specific corpus promotion or Required CI claim follows from them.
- Added a native Execute Package Task graph probe. It verifies executable containment and the explicit unresolved-target gap without inventing a package target or executing the task; positive target extraction remains future work.

## 2026-11-05 Status Matrix and Roadmap Separation

- Split current operation maturity/evidence into `STATUS.md` and ordered future work into `PLAN.md`; the roadmap now treats the mutation lifecycle as mandatory and keeps SSISDB/SQL Agent deployment as an independent subsystem.
- Expanded `ai variable.properties` to report variables in inspectable nested executable scopes with scope native IDs and incomplete-coverage reporting for unreadable or uncertain ownership. Variable mutations remain package-scope-only.
- Expanded `ai mutation.capabilities` with each advertised operation's lifecycle requirements and documented variable-operation limits. The focused Release integration build and native harness pass; the full gate and provider-required status still need the evidence/configuration described in `STATUS.md` and `PLAN.md`.
- Added exact-path failure coverage: component-configuration failure rolls back to the original path endpoints and component inventory, and a duplicate Union All output name is refused during adapter preflight before mutation. Derived Column host tests inject save/reload failures and reject an unexpected package-identity delta using host/validator test doubles. The focused Release build and MutationHost/path-splice probes pass. Rollback after failures during/after native adapter refresh and corresponding Data Conversion failure cases remain open.
- Added designer-layout assertions that zero-width and negative-height placement intents are rejected. The Release integration build and `--designer-layout-probe` pass; endpoint-adapter rejection and insertion-specific save/reload/delta failure cases remain open.

## 2026-11-05 Variable Properties and Description Update

- Added read-only `ai variable.properties` for package-scope variables, including native IDs, scope, type, read-only/system/expression flags, and explicit `Unclassified` sensitivity. Values and expression text are omitted; nested executable scopes return `coverage.variable_scopes_not_inspected`.
- Added prototype `set_variable_metadata` for the package variable Description field only. It binds to a native ID and source SHA-256, previews field names without old/new text, and uses the existing checkpoint/save-as/reload/offline-validation/semantic-verification lifecycle. The plan preserves other user-variable metadata and excludes transient SSIS system variables from its rebind signature.
- Added prototype `set_variable_expression` for one package-scope variable. It preflights bounded expression syntax and uniquely resolved references, enables `EvaluateAsExpression`, then verifies the exact resolved dependency after save/reload and offline validation. Malformed or unresolved references are refused; expression text is omitted from preview/results.
- Added prototype `set_variable_value` using a process environment variable name as the trusted input reference; direct JSON values are rejected. The operation refuses system, read-only, expression-owned, nested-scope, and unsupported-type targets and never serializes the environment name or value. Sensitivity remains unclassified, so this is not a supported secret-writing path.
- The native harness verifies description, expression, and environment-backed value persistence; unchanged source hash; stable IDs; unchanged sibling values/expressions; direct JSON value rejection; and redacted output. Negative expression cases cover malformed syntax and missing references. The full SSIS gate passes with 202 runtime, 13 corpus, and 23 mutation tests; 30 lifecycle cases and all native stages pass. The known `CS8012` warning remains. Nested-scope discovery and sensitivity classification remain future slices.

## 2026-11-05 Structural Corpus and Graph Regression Gate

- Added seven checked-in layout packages as a starter structural corpus with approved snapshots under `tests/LayoutFixtures/CorpusBaselines`. `Invoke-CorpusBaselineGate.ps1` repeats each CLI snapshot, verifies semantic equality and approved-baseline match, and never writes or approves baselines. Known coverage gaps remain part of the snapshots; matching incomplete snapshots are accepted with their gaps visible.
- Wired the read-only corpus gate into `Invoke-Ssis16MutationGate.ps1`. The native harness now repeats snapshot analysis and independently recomputes the fingerprint for the same package, requiring stable results.
- Added exact portable dependency-graph assertions for package/task containment, Execute SQL Task connection and schema-object edges, precedence, Data Flow containment/path topology, runtime connections, and input/output columns. Existing native probes cover scoped expression references and parameter evidence. Execute Package targets remain unmodeled; recognized Execute Package tasks now emit `dependency.execute_package_target_unresolved` rather than implying there are no dependencies.
- Added typed `rename_task` CLI preview/apply using the existing MutationHost lifecycle. Native integration verifies name redaction, source preservation, unchanged task ID/count, save/reload, offline validation, and semantic verification.
- The full `SSIS16-Mutation-Gate` passes after integration: 202 runtime, 13 corpus, and 23 mutation tests; seven structural corpus baselines; default native harness; 30 MutationHost lifecycle cases; designer/layout; 15 resize cases; exact path splice; and Union All offline validation. The build retains the known `CS8012` interop warning.
- The current corpus contains structural fixtures only, not representative business packages. Add sanitized, representative packages and review their initial baselines before treating this seed corpus as broad regression authority.

## 2026-11-05 Mutation Contract Schema 2.0

- Replaced the mutation-only 1.x contract versions with exact schema `2.0`; legacy 1.x mutation requests are rejected. Corpus, inspection CLI, and FlowRunner schemas remain unchanged.
- Migrated AI mutation requests to strict operation-specific payload DTOs and typed `TaskHost`/`DataFlowPath` targets. The caller must provide a SHA-256 source hash matching the package before preview or apply; lifecycle requirements and adapter selection remain host-owned.
- Mutation preview/apply now serialize through the common immutable `MutationEnvelope`, reporting host runtime and selected adapter, artifact/reload/validation/semantic/layout stages, rollback, diagnostics, coverage, and redaction. Source artifacts remain separate from published destinations; layout-specific failure details are reported as unverified when MutationHost cannot distinguish them.
- The complete `SSIS16-Mutation-Gate` passes on Windows x64 SSIS 16: Release solution build, 236 portable tests, default native harness, all 30 MutationHost lifecycle cases, designer/layout, 15 resize cases, exact path-splice fixtures, and clean Union All validation. The build retains the known `CS8012` interop warning. No package is executed or data source connected.

## 2026-11-05 Provider-Neutral SSIS 16 CI Gate

- Added `eng/ci/Invoke-Ssis16MutationGate.ps1` as a provider-neutral Windows x64 gate runner. It executes the Release solution build, all portable suites, default native integration/fixture harness, MutationHost lifecycle, designer/layout, column-resize, exact path-splice, and Union All probes; any nonzero stage stops the gate. Package execution probes remain excluded.
- Added standalone `--path-splice-probe` and `--union-all-probe` entry points so the two path mutation checks report independently. The runner emits `SSIS16-Mutation-Gate: PASS` only after all stages succeed.
- The complete gate passed on this Windows x64 SSIS 16 host: 236 portable tests, 30 MutationHost lifecycle cases, 15 resize cases, default native harness, designer/layout, path splice, and clean Union All validation. The Release build has the known `CS8012` interop warning.
- No CI provider workflow or branch-protection settings are committed. Configure the existing provider to run this script as the required `SSIS16-Mutation-Gate` PR job; GitHub Actions is not assumed.

## 2026-11-05 Union All Clean Validation Fixture

- Replaced the unconfigured synthetic OLE DB sources in the Union All acceptance probe with two native Flat File Sources using a temporary local ANSI ASCII file and a configured Flat File Connection Manager. The fixture establishes a clean offline validation baseline without database access or package execution.
- The probe now requires clean pre-mutation validation, clean in-memory post-splice validation, and clean post-save/reload validation; it verifies `OfflineMode` is restored. The temporary source file and package artifact are removed on success or failure.
- Preservation assertions cover the prior Union All output metadata and mappings, selected and nonselected inputs, unrelated paths, existing component IDs/classes, package identity, and designer layout before and after reload. Only the selected incoming path may be replaced; the adapter owns metadata refresh and `DataFlowPathSplicer` remains topology-only.
- Microsoft Learn documentation was checked by namespace for `Package.OfflineMode`, `IDTSRuntimeConnection100.ConnectionManagerID/ConnectionManager`, `IDTSDesigntimeComponent100.ReinitializeMetaData`, Flat File Source/Connection Manager, and the Runtime HRESULT `DTS_E_NTEXTDATATYPENOTSUPPORTEDWITHANSIFILES`. The source uses ANSI data to match its default `DT_TEXT` error output, avoiding the documented `DT_NTEXT`/ANSI incompatibility.
- The SSIS 16 integration build and full native harness pass; all 236 portable solution tests pass. The build retains the known GAC_32 `CS8012` interop warning.

## 2026-11-04 Union All Selected-Input Insertion

- Added `UnionAllInputAdapter`, resolving the native component CLSID through the installed SSIS catalog. It replaces only the selected Union All input, preserves its existing `OutputColumnLineageID` mappings, maps the inserted output, refreshes metadata, and verifies unaffected inputs and prior outputs. `DataFlowPathSplicer` now enforces the adapter verification result and retains splice/rollback exception details.
- The native synthetic probe is an acceptance gate: failures propagate with full exception details; it checks prior output metadata, selected and nonselected input mappings, unrelated paths, output inventory, and save/reload. SSIS may regenerate lineage IDs on reload, so round-trip checks validate mappings against the current named outputs.
- The initial unconfigured OLE DB probe reported four baseline validation errors; the configured, clean-validation acceptance fixture and final verification are recorded in the 2026-11-05 entry above. No package is executed and no database connection is made.
- The SSIS 16 integration build and full native harness pass; 23 portable mutation tests pass. The build retains the known GAC_32 `CS8012` interop warning.

## 2026-11-04 Exact-Path Data Flow Splicing

- Added `DataFlowPathSplicer` and task-scoped `pathId` support to Derived Column and Data Conversion plans. The plans derive endpoints from the selected path, require one inserted input and one regular output, splice only that edge, and preserve unrelated paths and component metadata.
- Removed the single-pipeline/task and two-component/one-path package gates. Existing endpoint-pair requests remain compatible; new requests use `pathId` from the selected task's `lineage.paths` list. Plan rebinding uses stable endpoint-based layout identity, and previews expose that stable identity rather than transient numeric path IDs.
- Changed fallback layout path identity to an endpoint-port pair so duplicate or regenerated SSIS path names/refIds cannot collide when comparing crossed paths.
- Added native fixture verification for Branch, the Merge output path, CrossedPaths, and pathless NestedDataFlows refusal. The fixtures are loaded from the repository and edited only in temporary copies; source fixtures remain unchanged. Union All input paths still require a specialized metadata-refresh adapter.
- The integration project builds; the default SSIS 16 harness and all 30 MutationHost lifecycle cases pass on the installed host. The build retains the known GAC_32 `CS8012` interop warning.

## 2026-11-04 Native TaskHost Mutation Operations

- Added `TransferTablesMutationPlan` over the native `ITransferSqlServerObjectsTask.TablesList` API. It supports explicit `Replace`, `Add`, and `Remove` modes, preserves unrelated task settings, binds to the task identity and source artifact hash, and previews before/after selection. `Replace` disables catch-all `CopyAllObjects`/`CopyAllTables`; `Add` and `Remove` refuse when those flags make selection semantics ambiguous.
- Generalized the MutationHost package-plan contract and exposed `update_transfer_tables` through `ai mutation.capabilities`, stdin-based `ai mutation.preview`, and `ai mutation.apply`.
- Added native scratch tests for Transfer task TablesList save/reload, each edit mode, capability/schema discovery, preview, and host checkpoint/save/reload/verify lifecycle. No package Execute or database connection is used; the user-provided EAB package is not modified.
- Added generic `ai task.properties` discovery plus typed `set_task_property` and `update_task_collection` preview/apply plans for writable TaskHost scalars and StringCollections. Enum-valued properties expose names and accept enum names instead of guessed numeric codes. Transfer collection modes update/refuse on their corresponding CopyAll flags; `TablesList` remains routed through its specialized plan. Native tests cover scalar apply and enum/property discovery without database connections.
- Fixed a duplicated StringCollection mode-schema description that prevented the CLI from compiling. The Release solution build and documented gates pass: 200 runtime tests, 13 corpus tests, 23 mutation-contract tests, the default SSIS 16 harness, and all 30 MutationHost lifecycle cases. The build retains the known `CS8012` warning for the installed SSIS `DTSRuntimeWrap` interop reference.

## 2026-11-03 Designer Layout Round-Trip

- Added read-only extraction of SSDT `DTS:DesignTimeProperties` payloads from a bounded source DTSX parse. Actual Control Flow layout uses hierarchical `refId` paths and fractional coordinates; coordinates now retain decimal precision.
- Added TaskHost component NodeLayout extraction and EdgeLayout curve sampling. The runtime normalizes design-time IDs to native numeric IDs after load; extraction accepts both forms. The public `Package.DesignTimeProperties` setter supports adding/updating a native component NodeLayout without editing DTSX XML.
- Added immutable placement intents (`RightOf`, `Below`, `OpenSpace`) and a deterministic previewer that refuses missing/ambiguous targets or unavailable coordinates.
- Added a source-hash-bound `DerivedColumnInsertionPlan`, stable layout-path rebinding across SSIS numeric-ID changes, a versioned/redacted preview JSON contract, and a MutationHost execution branch using the existing checkpoint, save-as, reload, validation, and semantic-verification pipeline.
- Added `DataConversionInsertionPlan` for explicit `DT_WSTR`, `DT_STR`, integer, and floating-point conversions with source lineage mapping, stable placement, and the same host lifecycle.
- Added `FileMutationCheckpointService` with create-new semantics, source artifact hash verification, cancellation cleanup, and overwrite refusal; lifecycle coverage verifies byte-identical checkpoint output and safe refusal on repeated operation IDs.
- Added `--verify-designer-fixtures`, which saves each SSDT-authored fixture to a temporary artifact, reloads it, compares extracted positions/endpoints/routes, and confirms the authored source is unchanged.
- All seven SSDT-authored fixtures pass native save/reload comparison. `TwoComponentHorizontal` includes two positioned Data Flow components and a 20-point sampled path curve.
- The native scratch harness resolves a `RightOf` intent. MutationHost lifecycle cases checkpoint, insert positioned Derived Column and Data Conversion components, publish separate artifacts, reload, run an injected validator, preserve existing component metadata/layout/routes, and verify output mappings and positions. No DB connection or package Execute is used. The production file checkpoint is implemented; native validation remains an injected test double in this harness.
- The focused designer-layout probe, default native scratch harness, 25-case MutationHost lifecycle probe, and portable suites pass on this SSIS 16 x64 host (200 runtime, 13 corpus, 23 mutation tests). The build retains the known GAC_32 `CS8012` warning. AI tool/client routing and a production native-validation policy remain outstanding.

## 2026-10-07 Component Support Tooling

- Added developer FlowRunner `describe`, `probe`, `compare`, and `catalog validate` commands. Native Derived Column/Data Conversion descriptions expose initialized ports and synthetic column-template defaults; defaults remain observations rather than inferred mandatory configuration.
- Added bounded structural XML comparison with deterministic changes, formatting/prefix normalization, visible generated IDs and preserved leaf text; no SSIS semantic certification or external entity resolution. Five portable comparison tests pass.
- Native tooling/probe regressions pass descriptions, missing-selector errors, valid/invalid catalogs, comparison, named recipe mismatch, actual native SSIS overflow messages and expected/actual value diagnostics. Cleanup failure reporting now preserves the original failure. Inspection CLI behavior remains separate and unchanged.

## 2026-10-07 RenameTask Host Lifecycle

- Added an opt-in scratch-package MutationHost lifecycle suite with 20 cases covering native save/reload/publication, stale names/hashes, target lookup, required obligations, checkpoint failure/hash mismatch, save/reload failure injection, validator/diff rejection, destination races, source locking, cancellation, and nested/event-handler targets. Checkpoints are test artifact copies and validators are test doubles, not native Validate/Execute.
- Tests exposed blanket task-property omission blocking all previews, data-flow companion renames being rejected, snapshots being computed before validator callbacks, and event-handler targets missing from executable discovery. Added an explicit omission code and rename-scoped policy, preserved gap/fingerprint completeness evidence, allowed only the target executable/data-flow names, rebuilt post-validator snapshots, and traversed handler executable inventories.
- Host packages are now disposed on normal/error paths; source read sharing spans loading through publication. Production checkpoint/validation services, result serialization, and authorization/journaling remain unverified or unimplemented.
- Hardened the lifecycle suite to 22 cases with a gated, genuinely asynchronous checkpoint callback and test-only malformed duplicate native IDs. The async test proves the operation was pending before release and that copying/resumption runs off the caller thread; the duplicate-ID case verifies refusal before checkpointing/publication. Both pass on this host. Added a documented required native regression gate; no SSIS-enabled CI runner or branch-protection check is configured yet.

This document records completed milestones, investigation evidence, and dated verification results. For current operation maturity and evidence, see [STATUS.md](STATUS.md). For ordered remaining work and design decisions, see [PLAN.md](PLAN.md). Runtime behavior and safety limits are in [README.md](README.md); build and verification commands are in [BuildPackage.md](BuildPackage.md).

## 2026-10-07 Synthetic SSIS Execution

- `FlowRunner components` now discovers all local pipeline registrations without instantiation or execution. This host exposes 59 registrations; exact native catalog coverage, deterministic output, separate recipe/configuration/test-availability flags, and no false per-invocation execution claims pass native regressions. Three selected registrations have synthetic-flow recipes; the remaining catalog entries are discovery-only.

- Added a separate `SsisAiRuntime.FlowRunner` x64 net48 executable. Its explicit `demo` recipe runs native SSIS Flat File Source -> Derived Column -> Flat File Destination over three synthetic integers, checks exact output values and row counts, and creates no database connections.
- The recipe runs in a child process with a 60-second timeout. The parent cleans its temporary input/output directory after completion and emits fixed status codes without native error text or paths. This is trusted developer execution, not a security sandbox or a generic flow-specification implementation.
- Opt-in native harness mode `--execute-flow-probe` passes real runner execution, output assertions, redacted JSON, usage rejection, and cleanup checks. The default native metadata harness remains non-executing. Build retains the known interop processor warning.
- Added bounded JSON-stdin `run` requests for Int32 input rows, a native Derived Column expression, and explicit expected values. Native regressions pass multiplication and conditional expressions, expected-result mismatch, malformed expressions, incompatible schemas, and cleanup. Ten portable parser tests pass without native SSIS dependencies. General flow topology, scripts/custom components, and database probes remain unimplemented.

## 2026-10-07 In-Memory Pipeline Edits

- Native SSIS 16 x64 harness passes a new contrived, in-memory source/destination fixture: destination input remapping, insertion of a Derived Column into the existing flow, path reconnection, and calculated-column destination mapping.
- Structural assertions against native `SaveToXML` output verify input lineage/external mappings, the integer output type, the expected source reference and expression, component/path inventories, path endpoints, and unchanged source metadata. Native `LoadFromXML` accepts the edited XML and preserves package identity, topology, and destination mappings.
- The new fixture performs no file writes, database connections, package execution, or validation. Editing is test-only; no production executor or mutation-host API is implemented by these tests.
- The complete native integration harness passes on this host. Build retains the known GAC_32 interop `CS8012` warning. Diagnostics for the new fixture and harness entry point are clean.

## 2026-10-05 Parsed SQL and Context

- Full portable suite after SQL/context implementation: **174 passed, 0 failed, 0 skipped**.
- Added ScriptDom-backed T-SQL lexing/parsing and connection-scoped schema-object dependencies, with parsed evidence retained through selectors, classified impact, and AI facts. Regex dependency extraction remains an external prototype, not a production source.
- Native SQL fixtures verify stock task loading, ID-based connection resolution, SQL Server provider detection, comment/literal exclusion, dynamic gaps, graph isolation across two connections, and unchanged hashes without opening a database connection.
- Added bounded metadata-only `ai context`, with explicit `--include-sanitized-text` collection/emission. SQL and expression masking is parser/token-based, failure omits text, and snippets are labeled untrusted syntax rather than graph evidence.
- Native CLI regressions verify default omission, sanitized SQL/expression opt-in, literal masking, bounds, invalid-option rejection, and no leaked fixture values. Native column and scope fixtures remain unchanged in behavior.
- The portable suite also includes the strengthened unwrapped-variable regression: `User::Counter`, `Other::Counter`, and `System::Counter` all remain candidates. This exposed and fixed exclusion of projected system symbols from unwrapped lookup.

## 2026-10-05 Earlier Verification

- Full portable suite before the additional namespace and SQL/context regressions: **150 passed, 0 failed, 0 skipped**. This is a historical checkpoint, not the current suite size.
- Native SSIS 16 integration build succeeds without assembly-path overrides. The installed GAC_32 interop reference produces the known `CS8012` processor warning; the x64 verifier passes on this host. This does not certify other installations.
- Native scratch fixtures pass for Data Conversion, Derived Column, expression syntax, scope resolution, and actual CLI subprocess behavior.
- Owner regressions verify package-variable, task, connection, and event-handler expression owners against the same catalog and graph identities. They cover handler-variable shadowing and reuse of one package parameter by package/task/handler owners.
- Portable graph tests reject project references without explicit project context or without a projected project target. Project inventory resolution uses explicit projections; no project-backed SSIS fixture is claimed.
- Inspections preserve package hashes and redact expression text, values, and native error details. No package execution, validation, or metadata refresh is used for extraction. Scratch-package creation and save/reload are test-only.
- Editor diagnostics and `git diff --check` pass for the changed files.

## Native Binding Investigation

Status: closed on 2026-10-05. Variable/parameter edges are heuristic, not native runtime bindings.

### Column Observation

`IDTSExpressionEvaluatorEx100.Parse(expression, variableDispenser, inputColumns)` validates syntax and exposes data-flow column references through observed `GetInputColumnByLineageID` and `GetInputColumnByName` calls. The production adapter uses a read-only forwarding column collection, refuses collection mutations, and discards provisional references after parse failure.

Native cases passed for single/multiple/named/repeated columns, variable-driven conditionals, constants, quoted/escaped strings, and malformed syntax. Derived Column fixtures passed per-output mappings, literal-only outputs, original-value replacement semantics, task-scoped variable shadowing, and CLI redaction. A resolved empty column-reference list does not establish independence from variables.

### Variable and Parameter Limits

- Package-, task-, and nested-scope variable expressions parse, but produce no variable-dispenser read requests or resolved-variable callbacks.
- Same-qualified-name variables were created in distinct package/task/nested scopes; native `Parse` did not reveal which declaration was chosen.
- Wrapped package-parameter syntax parses without a binding callback. Unwrapped package-parameter syntax fails on this host.
- Project-parameter syntax cannot be certified through the standalone-package fixture; project-backed metadata is not available there.
- The low-level variable interface exposes namespace, qualified name, and parent information, but no native variable ID. The production scope projection instead retains IDs from the managed native metadata.
- `Package.FindReferencedObjects` exists in the API but throws `NotImplementedException` on this SSIS 16 runtime.
- Constants produce no callbacks; callback absence alone is not proof that an expression has no dependencies. Malformed expressions discard provisional references.

Decision: retain native column observation for lineage. Use ANTLR lexical candidates plus separate deterministic scope resolution for variable/parameter discovery. Only uniquely resolved, projected targets become `LexicalAndScopeResolved` heuristic edges. Unresolved references remain redacted coverage gaps.

### Unwrapped Syntax Probe

SSIS 16 and ANTLR agree on the tested forms:

| Form | Syntax Result |
| --- | --- |
| `@ParserFlag` | Accepted |
| `@[User::ParserFlag]` | Accepted |
| `@[$Package::ProbeParameter]` | Accepted |
| `@User.ParserFlag` | Rejected |
| `@User::ParserFlag` | Rejected |
| `@Package::ProbeParameter` | Rejected |
| `@$Package::ProbeParameter` | Rejected |
| `@Parser$Flag` | Rejected |
| `@Parser#Flag` | Rejected |

The narrow unwrapped-reference lexer was retained. These observations prove syntax acceptance only, not native variable/parameter binding or complete SSIS language coverage.

## Completed Milestones

- Removed the legacy WinForms/Explorer project and old solution at the user's request; retained the existing license.
- Established portable .NET Standard 2.0 Core/inspector/AI projects, a .NET Framework 4.8 SSIS 16 adapter and x64 CLI, and portable .NET 10 tests.
- Implemented native package loading, immutable redacted inspector projections, explicit completeness/unsupported reporting, and focused SQL/lineage/configuration contexts.
- Added recursive executable hierarchy, session-scoped semantic handles, ambiguity-aware resolution, containment/precedence graphs, native column tracing, and metadata-only search.
- Implemented the read-only CLI with bounded summaries, `--details`, aggregate `inspect`, and report selection through `--include`.
- Unified deterministic AI tools over immutable package snapshots: summaries, dependency graphs/queries, selectors, classified impact, and question planning. No LLM calls are embedded.
- Added separated ANTLR candidate parsing, native declaration-scope projection, nearest-scope resolution, heuristic dependency enrichment, and evidence-preserving CLI/AI output.
- Added a partial-selector regression that preserves exact candidate totals while limiting returned candidates.
- Replaced the smoke-script wrapper with a native C# verifier. The personal inspection skill remains user-level, outside this repository.

## Earlier Host and Package Checks

These earlier milestones have no separate recorded date here. Their counts describe specific fixtures and inspector stages, not the current portable suite total.

- Windows Release solution builds passed with default assembly resolution and explicit DLL-path overrides. This host's SSIS 16 assemblies were found in the GAC rather than Binn/SDK; the adapter gained standard Binn/GAC fallbacks and full-path overrides.
- A loader smoke check reported native assembly version `16.0.0.0`, x64 process architecture, and zero diagnostics.
- The initial Core subset passed 4 tests on Windows and, earlier, 4 on macOS.
- Initial WebProd overview checks found 9 connections, 25 variables, 2 root executables, and 1 root precedence constraint. Connection/variable omission assertions passed without package execution.
- Recursive WebProd inspection represented all 21 executables, including 2 roots and 2 sequence containers, with no missing parent or creation-name metadata.
- Broader WebProd checks recorded 0 parameters, 9 SQL tasks, 9 data flows, 18 components, 9 paths, 18 runtime connections, 122 input columns, 284 output columns, 246 external metadata columns, and 180 settings (1 redacted).
- Paycom2 verification resolved 23 Derived Column outputs without raw-expression fields and preserved the package hash.

Historical package checks establish observed coverage on those fixtures only; they do not guarantee execution validity or support for arbitrary components.
