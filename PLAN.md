# Plan

This document orders future work and its dependencies. The operation-by-operation evidence ledger is in [STATUS.md](STATUS.md); dated implementation and verification records remain in [HISTORY.md](HISTORY.md).

## Completed Foundation

- SSIS 16 native loading, bounded read-only projections, control/data-flow graphs, lineage, SQL analysis, deterministic CLI services, and explicit coverage reporting are implemented.
- Mutation schema 2.0 provides typed requests, source-hash binding, host-selected adapters, previews, lifecycle results, and redaction. Implemented operations remain prototypes unless [STATUS.md](STATUS.md) shows the required evidence.
- `ai variable.properties` includes native scope identity for inspectable package and nested executable scopes. Variable mutations remain package-scope-only; sensitivity is unclassified.
- The provider-neutral `SSIS16-Mutation-Gate` runner covers portable suites, nine corpus baselines (seven layout fixtures plus two synthetic semantic fixtures), native probes, and mutation lifecycle tests. Provider configuration and branch protection are not present in this repository.

## Mandatory Mutation Framework

Every mutation declares applicable lifecycle requirements through its typed operation contract and `ai mutation.capabilities`. The common framework is mandatory; a step may be marked not applicable only when the operation contract explains why.

1. Validate the typed request and target.
2. Verify source identity and the caller's source-artifact hash.
3. Produce a redacted preview bound to that source hash.
4. Create a checkpoint or other recoverable source copy.
5. Apply the native mutation.
6. Save to a separate artifact by default.
7. Reload the saved artifact.
8. Run offline validation with the native runtime where applicable; package execution is not mutation validation.
9. Validate intended and unintended semantic/layout deltas where applicable.
10. Emit a redacted operation report with diagnostics and coverage.
11. Classify rollback accurately; a failed operation does not prove restoration succeeded.

Runtime/adapter identity, stage results, coverage, and redaction belong in the common result envelope. A lifecycle claim requires operation-specific test evidence, not merely the existence of the shared host.

## Current Hardening Milestone

Prototype slices may continue in parallel; the gates below control promotion, not whether adjacent work may start.

1. **Require the SSIS gate in CI.** Run `eng/ci/Invoke-Ssis16MutationGate.ps1` as a Windows x64 SSIS 16 job with stable status name `SSIS16-Mutation-Gate`, then configure that status as required in the provider. No workflow or branch-protection configuration currently exists in this repository, so the provider-side setup remains an external prerequisite for promotion.
2. **Maintain the operation evidence matrix.** Keep [STATUS.md](STATUS.md) aligned with checked-in native fixtures, lifecycle cases, approved corpus evidence, and provider configuration. Do not infer completion from an implementation or a passing local script alone.
3. **Advance project-aware and multi-input graph closure.** Do not expand CF-01 through CF-12 or VP-01 through VP-15 merely to increase coverage counts; their defined scopes have evidence. SQL-01 through SQL-05 are complete for the defined v1 matrix. Prioritize authoritative project context and package-parameter propagation, then native multi-input and asynchronous Data Flow lineage. Preserve current evidence and coverage semantics. The corpus still has seven layout fixtures and two native-generated synthetic semantic fixtures, not representative business packages; add the project and multi-input synthetic fixtures, then sanitized representatives when available.
4. **Promote `rename_task` only after its remaining gates pass.** It is the strongest first candidate because its intended semantic delta is narrow. Preserve the existing task ID/count, hierarchy, connections, expressions, SQL dependencies, and unrelated properties. Require reviewed representative corpus evidence and required CI before marking it Supported.
5. **Harden exact-path insertion.** `DataFlowPathSpliceFixtureTests` proves that a component-configuration failure triggers rollback, restores the original endpoint topology, and removes the inserted component; a Union All duplicate-output conflict is refused before mutation. `DesignerLayoutTests` rejects nonpositive placement dimensions. Derived Column host tests now inject save/reload failure and an unexpected package-identity delta; these use host/validator test doubles. Remaining work: rollback after failures during/after native adapter refresh and corresponding failure cases for Data Conversion. Preserve nonselected paths, component metadata, and layout.
6. **Establish trusted sensitivity classification.** Until a verifiable policy distinguishes safe from sensitive targets, do not promote writes to sensitive or unclassified variables. Keep the environment-backed value prototype redacted and unsupported for secret writing.

The current corpus has seven structural layout fixtures and two synthetic semantic examples, not representative business packages. Add `SyntheticProjectDependencies` and `SyntheticMultiInputLineage` as the next semantic fixtures. For `rename_task` promotion, target 3-5 sanitized packages from at least two projects, including nested control flow and a nontrivial Data Flow. For broader graph confidence, target 8-12 representative packages and review every unresolved item in each approved baseline. These are engineering targets, not universal standards or prerequisites for independent prototypes. Add sanitized artifacts only when available; review semantic diffs before explicitly approving baselines, and never present synthetic fixtures as business coverage.

### Next Graph Milestone: Project-Aware and Multi-Input Graph Closure

Deliver four outcomes: resolve cross-package dependencies only from authoritative project context; preserve parameter propagation across package invocation as a distinct binding relationship; extend native lineage through multi-input transformations; and never weaken existing evidence or coverage semantics. Keep this milestone independent from `rename_task` promotion: project-graph completion, Lookup/Aggregate lineage, and future SQL extensions must not block rename when its own invariants, representative corpus evidence, and required CI are satisfied.

## Graph Acceptance Matrix v1

This matrix is the frozen v1 acceptance contract. Its complete scope is CF-01..CF-12, VP-01..VP-15, and SQL-01..SQL-05. Do not add v1 rows to expand those scopes. Execute Package and Data Flow follow-on cases below are v2 planning material; retain their statuses without treating them as v1 acceptance rows. New scenarios go into v2. Each fixture records expected nodes, edge direction and kind, evidence class, implementation status, impact-query behavior, and negative behavior. A package snapshot may have unrelated coverage gaps; assert completeness for the tested slice rather than treating global `IsComplete` as the only result.

Use only these row statuses: **Complete**, **Partial**, **Open**, **Blocked by fixture**, **Deferred**, and **Not supported**. `Complete` means the row's stated assertions have evidence; it does not imply support beyond that fixture or evidence class. `Partial` means only part of the row is asserted. `Open` means no adequate assertion exists. `Blocked by fixture` means the capability cannot be tested until an identified fixture/runtime dependency is available. `Deferred` is intentionally out of the current milestone. `Not supported` is an explicit product boundary.

### Current Status

| Area | Assessment | Remaining |
| --- | --- | --- |
| Control Flow CF-01..CF-12 | Complete for the defined matrix | Regression maintenance |
| Variables/Parameters VP-01..VP-15 | Complete for current architecture | Discover project parameters from actual project inventory |
| SQL SQL-01..SQL-05 | Complete for the defined matrix | Future SQL matrix is separate |
| Execute Package | Partial; declared references, supplied-inventory resolution, typed assignment projection, and binding graph are in place | Real project discovery, candidate reporting, real-project verification, and bounded transitive traversal |
| Data Flow lineage | Partial | Multi-input, asynchronous/Aggregate, Lookup, and exact failure attribution |
| Corpus graph regression | Synthetic foundation established | Project/multi-input fixtures and representative sanitized packages |
| Mutation graph verification | Strong shared foundation | Operation-specific representative corpus evidence |
| CI | Gate passes locally | Provider must require `SSIS16-Mutation-Gate` |

Keep evidence authority distinct from coverage status:

| Evidence authority | Source | Examples |
| --- | --- | --- |
| `Native` | Authoritative SSIS object model | containment, precedence, pipeline paths, native column mappings |
| `Parsed` | Deterministic parser over native-projected text | ScriptDom schema objects, parsed expression references |
| `ResolvedHeuristic` | Deterministic lexical candidate plus bounded scope resolution | variable/parameter references without native binding proof |
| `Declared` | Explicit package metadata that has not been resolved to an authoritative target identity | `DeclaredProjectReference`, external reference declarations |

These are provenance categories, not confidence scores. Preserve specific evidence codes such as `ParsedSchemaObject` or `DeclaredProjectReference` independently from any normalized authority field.

Do not flatten these relationships into a generic `DependsOn` edge. Preserve current owner-to-dependency orientation; answer “what consumes this?” through incoming/impact queries instead of adding duplicate reverse edges. For every positive edge fixture, assert direct and recursive impact/query results and that the expected path is retained. For ambiguous, missing, or dynamic targets, assert no guessed edge, explicit candidates or coverage, and no fabricated impact path. Expression-derived variable/parameter edges remain heuristic and never replace native column-lineage evidence.

Evidence and coverage are orthogonal. A declared invocation edge may be present while its target remains unresolved and a coverage gap is reported. Conversely, extraction failure may produce a gap without an edge. Graph completeness must not mean every reference resolved; represent known relationships faithfully and report unknowns separately.

### A. Control-Flow Structure

| ID | Fixture | Required assertions | Evidence class | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| CF-01 | Two unconnected package tasks | Two task nodes and package containment edges; no execution-order edge | Native | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| CF-02 | Linear Success constraint | Exact endpoints, `DTSExecResult.Success`, one precedence edge | Native | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| CF-03 | Failure constraint | Exact endpoints and Failure result | Native | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| CF-04 | Completion constraint | Exact endpoints and Completion result | Native | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| CF-05 | Expression-only constraint | Preserve `EvalOp=Expression`; emit expression dependencies separately | Native + parsed | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| CF-06 | Expression-and-constraint | Preserve `ExpressionAndConstraint`, execution result, references, and AND semantics | Native + parsed | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| CF-07 | Expression-or-constraint | Preserve `ExpressionOrConstraint`; never report logical AND | Native + parsed | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| CF-08 | Multiple incoming constraints | Preserve each predecessor and `LogicalAnd`; do not collapse fan-in | Native | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| CF-09 | Sequence container | Package contains Sequence; Sequence contains tasks; internal edges stay scoped | Native | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| CF-10 | Nested Foreach and Sequence | Correct multi-level containment and nearest-scope resolution | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| CF-11 | Event-handler flow | Separate handler scope with its own executables and constraints | Native | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| CF-12 | Missing endpoint | Gap is attached to the unresolved endpoint; never bind by display name | Portable | Complete | [ControlFlowGraphBuilderTests.cs](tests/SsisAiRuntime.Tests/ControlFlowGraphBuilderTests.cs) |

Assert the native constraint properties (`Value`, `EvalOp`, `Expression`, and `LogicalAnd`) directly. Do not infer semantics from designer connector color or labels.

All CF rows have evidence for their stated assertions. This does not claim complete coverage of every SSIS executable or event-handler type.

### B. Variable and Parameter Dependencies

Variable scope is package/container/task/event-handler based. Parent declarations are visible downward; task declarations are not visible outside their owner. Names are case-sensitive unless a native behavior test proves otherwise. Package and project parameters are distinct node kinds.

| ID | Fixture | Required assertions | Evidence class | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| VP-01 | Package variable in task property expression | One owner-to-variable dependency with exact owner/declaration IDs | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-02 | Container variable in nested task | Bind to nearest visible declaration | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-03 | Same name at package and container scopes | Nested owner binds to container declaration | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-04 | Same-name sibling-scope variables | No cross-sibling edge | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-05 | Task variable referenced outside task | No edge; retain unresolved/out-of-scope evidence | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-06 | System variable reference | Typed system-variable node and dependency | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-07 | Package parameter reference | Typed package-parameter node, not variable node | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-08 | Project parameter dependency from supplied metadata | Typed project-parameter node and dependency when authoritative metadata is supplied | Portable + native host harness | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs); [DependencyAndPlanningTests.cs](tests/SsisAiRuntime.Tests/DependencyAndPlanningTests.cs) |
| VP-09 | Project parameter without project metadata | Candidate retained; no target edge; coverage gap | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-10 | Mixed variable/parameter expression | Independent edge for each resolved reference | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-11 | Ambiguous unqualified reference | No guessed edge; candidates listed | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-12 | Case mismatch | No match under current heuristic policy; no native binding claim | Portable + heuristic | Complete | [ExpressionScopeResolverTests.cs](tests/SsisAiRuntime.Tests/ExpressionScopeResolverTests.cs); [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-13 | Sensitive parameter | Retain dependency; redact value and default | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-14 | Expression-owned variable references another symbol | Link the referenced symbol to the variable-expression owner without raw text | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| VP-15 | Chained variable expressions | Recursive impact reaches indirect consumers without adding a direct edge | Native + heuristic | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |

Project-aware maturity is split deliberately:

| Capability | Evidence class | Status | Evidence |
| --- | --- | --- | --- |
| Construct a project-parameter dependency from supplied project metadata | Portable + native host harness | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs); [DependencyAndPlanningTests.cs](tests/SsisAiRuntime.Tests/DependencyAndPlanningTests.cs) |
| Resolve a project reference against supplied inventory metadata | Portable + native host harness with synthetic inventory | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs); [DependencyAndPlanningTests.cs](tests/SsisAiRuntime.Tests/DependencyAndPlanningTests.cs) |
| Discover project parameters from a loaded `.dtproj` or equivalent project inventory | None | Open | No project-loading fixture or inventory provider |
| Discover and project package inventory from a loaded `.dtproj` | None | Open | No project-loading fixture or native project provider |

VP-08 proves graph construction from injected host metadata; it does not prove `.dtproj` discovery or project loading. Add parser cases for `@ParserFlag`, `@[User::ParserFlag]`, `@[$Package::ProbeParameter]`, and `@[$Project::ProjectParameter]`; also casts, conditional operators, function arguments, concatenation, nested parentheses, string literals resembling references, and comments/escaped strings where supported. SSIS expressions may be owned by variables, constraints, For Loops, task/connection properties, Conditional Split, Derived Column, Execute SQL properties, and Execute Package targets. `@` references and `#` Data Flow lineage identifiers are distinct token classes; `#138` must never become a variable candidate.

Add parser cases for `@ParserFlag`, `@[User::ParserFlag]`, `@[$Package::ProbeParameter]`, and `@[$Project::ProjectParameter]`; also casts, conditional operators, function arguments, concatenation, nested parentheses, string literals resembling references, and comments/escaped strings where supported. SSIS expressions may be owned by variables, constraints, For Loops, task/connection properties, Conditional Split, Derived Column, Execute SQL properties, and Execute Package targets. `@` references and `#` Data Flow lineage identifiers are distinct token classes; `#138` must never become a variable candidate.

### Follow-on Matrix v2: Execute Package Relationships

An Execute Package invocation is not containment or precedence. Add a distinct `InvokesPackage` relationship only when the target evidence supports it. Keep child parameter bindings separate from invocation edges.

| ID | Fixture | Required assertions | Evidence class | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| EP-01 | Project-reference child present | Resolve exact child package and emit `InvokesPackage` only from authoritative inventory | Portable + native host with supplied inventory | Partial | Unique supplied-inventory match is tested; no loaded-project fixture |
| EP-02 | Project-reference target absent | Preserve declared name; unresolved-target gap | Native + portable | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs); [DependencyAndPlanningTests.cs](tests/SsisAiRuntime.Tests/DependencyAndPlanningTests.cs) |
| EP-03 | Duplicate candidate package names | No guessed edge; report ambiguity and candidate identities | Project inventory | Partial | Duplicate matches are refused with explicit ambiguity coverage; candidate identities are not yet projected |
| EP-04 | Literal file-system package path | External reference node with normalized evidence; do not imply loaded child | Native | Open | No file-reference fixture |
| EP-05 | File path supplied by expression | Link expression inputs; target remains dynamic | Native + heuristic | Open | No dynamic target graph assertion |
| EP-06 | SQL Server package reference | External catalog/storage reference node | Native | Open | No SQL Server reference fixture |
| EP-07 | Child package also in corpus | Optionally correlate corpus identity without claiming same-session load evidence | Corpus | Deferred | Corpus identity correlation is outside the current graph slice |
| EP-08 | Self-reference | Detect a cycle by project/package identity; do not recursively load | Portable project graph | Partial | Self-cycle is preserved and recursive queries terminate; no native project fixture |
| EP-09 | A invokes B, B invokes C | Bounded transitive impact traverses package calls using stable identities | Portable project graph | Partial | Multi-hop query reaches supplied package identities; no native project fixture |
| EP-10 | A invokes B, B invokes A | Cycle-safe traversal emits bounded cycle evidence and terminates without marking the graph incomplete solely for the cycle | Portable project graph | Partial | Mutual cycle edges remain and recursive query terminates; no native project fixture |
| EP-11 | Multiple parents invoke one child | Preserve every incoming invocation edge | Project inventory | Open | No resolved-child inventory fixture |
| EP-12 | Child parameter bindings | Preserve invoking task, parent source, target package, and child parameter as distinct binding semantics; do not substitute `UsesParameter` | Native assignment API + supplied inventory | Partial | Typed assignment projection and binding-node graph tests exist; no real project fixture |
| EP-13 | Disabled Execute Package task | Preserve invocation topology and disabled state | Native | Open | No disabled-task invocation assertion |
| EP-14 | Statically unresolved target expression | Dynamic-target gap and links to controlling variables/parameters | Native + heuristic | Open | No dynamic target graph assertion |

EP-02 remains complete for standalone inspection: a declared Project Reference is retained as an `ExternalPackageReference` node and `InvokesPackage` edge with `DeclaredProjectReference` evidence while an unresolved-target gap remains without inventory. With supplied inventory, one exact match additionally emits project-resolved invocation/reference edges. EP-01 and EP-12 are partial: the native assignment API and explicit synthetic inventory path are tested, but no real `.dtproj` is loaded. See [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs), [DependencyAndPlanningTests.cs](tests/SsisAiRuntime.Tests/DependencyAndPlanningTests.cs), and [PackageParameterBindingGraphTests.cs](tests/SsisAiRuntime.Tests/PackageParameterBindingGraphTests.cs).

#### Project Inventory Design

The runtime-neutral `IProjectInventory` contract and immutable project/package parameter projections are implemented. `PackageAnalysisSnapshotFactory.CreateWithProjectInventory` accepts this explicit context; the graph builder verifies that the analyzed parent package occurs exactly once, then resolves an Execute Package `PackageName` only against one exact project-relative-name match. The native SSIS 16 inspector uses typed `IDTSExecutePackage100.ParameterAssignments` and `IDTSParameterAssignment` interfaces to project child parameter names and parent variable/parameter references; it never reads assignment values. Each binding is represented by a `PackageParameterBinding` node: the invoking task has a `HasPackageParameterBinding` edge to it, it links to its scope-resolved parent source with `UsesVariable` or `UsesParameter`, and it targets the child parameter with `BindsPackageParameter`. This keeps invocation, source use, and child binding distinct. No `.dtproj` parsing, file discovery, or recursive package loading is implemented. A real SSIS 16 host projection from a loaded project remains open. Package identity retains project identity, project-relative package name, and package identity; the inventory also carries deployment model, and the task projection retains reference type.

Keep reference kinds distinct: `DeclaredProjectReference`, `ProjectInventoryResolved`, `ExternalFileReference`, `ExternalSqlServerReference`, and `DynamicPackageReference`. Project resolution adds identity evidence; it does not erase the original declaration.

Implementation order:

1. EP-01: add a real project-backed fixture to verify unique resolution through an SSIS 16 project provider; the supplied-inventory path is implemented but not `.dtproj` discovery.
2. EP-03: expose bounded candidate identities for duplicate matches; the current implementation refuses resolution and emits explicit ambiguity coverage.
3. EP-12: the typed assignment reader and binding-node graph are implemented and covered by synthetic native/portable tests. Add a real project fixture and verify CLI/corpus redaction before treating this row as complete.
4. EP-08 and EP-10: portable project graph composition and recursive queries preserve self/mutual cycle edges and terminate without unresolved gaps. Add bounded cycle-path reporting and a native project-backed case.
5. EP-09: project graph composition is explicitly bounded (128 package graphs, 20,000 nodes, 50,000 edges); add caller-facing traversal limits and project-impact reporting through AI/CLI.
6. EP-05 and EP-14: connect dynamic target expressions to their controlling symbols while keeping the target unresolved.
7. EP-04 and EP-06: model file-system and SQL Server references after project-reference semantics are stable.

The acceptance fixture should contain `GraphProject.dtproj`, `Project.params`, `Parent.dtsx`, and `Child.dtsx`; a project parameter used by both packages; a child package parameter; a parent Execute Package task bound from a parent variable or parameter; and a deliberately missing child reference. Use it for project-parameter discovery, EP-01, EP-02, and EP-12, while retaining separate fixtures for ambiguous inventory and cycles (`A -> B -> C`, `A -> B -> A`, `A -> A`). Assert bindings remain distinct from invocation and ordinary expression-use edges. Add deterministic project graph snapshots and expose project graph coverage through CLI and AI results. Keep child artifact loading explicit and bounded; ordinary package inspection must not recursively load packages. No package execution or source-artifact mutation is permitted, and all values and expressions remain redacted.

### Follow-on: Expression-Owner Coverage

Test parser/scope resolution independently from graph construction: a parsed candidate is not proof of binding. Add at least one owner fixture for each supported category.

| Owner | Minimum fixture |
| --- | --- |
| Variable expression | Variable depends on another variable and a package parameter |
| Task property expression | Execute Process or File System task property |
| Connection manager | Connection string/server property; output remains redacted |
| Precedence constraint | Boolean expression with two references |
| For Loop | Initialization assignment, evaluation read, and increment read/write distinction if supported |
| Derived Column | Native column lineage plus separate variable/parameter evidence |
| Conditional Split | Input-column dependency plus variable dependency |
| Execute SQL | SQL source property supplied by an expression |
| Execute Package | Dynamic path/name expression and unresolved child target |

### Follow-on Matrix v2: Data-Flow and Column-Lineage Graph

| ID | Fixture | Required assertions | Evidence class | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| DF-01 | Source to destination | Exact path and observed input/output column identities | Native | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| DF-02 | Derived Column creates output | New lineage and source-column dependencies | Native | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| DF-03 | Derived Column replaces output | Preserve replacement semantics and original-source dependency | Native | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| DF-04 | Conditional Split | One input lineage may feed multiple conditional outputs | Portable | Partial | [DataFlowOverviewTests.cs](tests/SsisAiRuntime.Tests/DataFlowOverviewTests.cs); native component coverage remains open |
| DF-05 | Data Conversion | Output traces to source with conversion relationship | Native | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |
| DF-06 | Union All | Multiple selected inputs map to one output; retain per-input mappings | Native | Open | Metadata probe exists; native lineage assertion remains open |
| DF-07 | Merge/Merge Join | Preserve separate left/right provenance | Native | Open | No provenance assertion |
| DF-08 | Lookup | Separate pass-through, lookup-derived, and error-output lineage | Native | Open | No Lookup semantics fixture |
| DF-09 | Aggregate | Do not misreport aggregate derivation as identity mapping | Native | Open | No Aggregate semantics fixture |
| DF-10 | Asynchronous transformation | New lineage IDs and explicit boundary | Portable + native | Partial | [DataFlowOverviewTests.cs](tests/SsisAiRuntime.Tests/DataFlowOverviewTests.cs); native transformation coverage remains open |
| DF-11 | Unattached output | Preserve metadata; do not invent downstream path | Portable | Complete | [DataFlowOverviewTests.cs](tests/SsisAiRuntime.Tests/DataFlowOverviewTests.cs) |
| DF-12 | Missing upstream metadata | Gap tied to exact component and port | Portable model | Partial | Exact native task/component/port attribution remains open |
| DF-13 | Custom component | Preserve native identity/topology; classify semantics unsupported | Portable model | Partial | Native custom-component topology fixture remains open |
| DF-14 | Expression uses variable and columns | Native column edges plus separately classified heuristic variable edge | Native | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) |

Every case must assert that heuristic enrichment neither upgrades nor replaces native column observations.

Split remaining Data Flow work into independently testable slices:

1. **Union All provenance:** Add distinct input-to-output `ExplicitMapping` edges for every contributing input. Keep ports distinct; do not collapse by column name; test duplicate names, multi-source upstream traversal, and bounded downstream traversal. Reuse component-mutation knowledge, but assert read-only lineage independently.
2. **Merge and Merge Join provenance:** Preserve left/right provenance when both contribute, and report one-source derivation when only one side contributes. Never infer a two-source dependency solely from the component having two inputs.
3. **Aggregate/asynchronous derivation:** Add a narrow `ExplicitDerivation` relationship from source columns to aggregate outputs; keep derivation distinct from identity and preserve asynchronous buffer boundaries. A broader derivation taxonomy is not a prerequisite.
4. **Lookup semantics:** Separate pass-through, reference-derived, no-match, and error outputs. Do not claim table-level lineage unless connection and query metadata independently support it.
5. **Exact failure attribution:** Attach each gap to Data Flow task, component native ID/name, input or output port ID, lineage ID when available, failure code, and evidence source.

For asynchronous transformations, recognize new lineage IDs and qualified derivation boundaries; do not label derivation as identity. Include Sort when the fixture environment supports it. Native Conditional Split coverage and a native custom-component topology fixture also remain open. See [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs), [DataFlowOverviewTests.cs](tests/SsisAiRuntime.Tests/DataFlowOverviewTests.cs), and [UnionAllMetadataProbe.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/UnionAllMetadataProbe.cs).

### C. SQL Dependency Graph (v1)

Continue connection-scoped ScriptDom evidence with these fixtures:

| ID | Fixture | Expected result | Evidence class | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| SQL-01 | SELECT from two-part name | Read dependency with connection scope | Portable | Complete | [SqlDependencyAnalyzerTests.cs](tests/SsisAiRuntime.Tests/SqlDependencyAnalyzerTests.cs); [SqlDependencyGraphTests.cs](tests/SsisAiRuntime.Tests/SqlDependencyGraphTests.cs) |
| SQL-02 | INSERT, UPDATE, DELETE, MERGE | Correct read/write classification | Portable | Complete | [SqlDependencyAnalyzerTests.cs](tests/SsisAiRuntime.Tests/SqlDependencyAnalyzerTests.cs); [SqlDependencyGraphTests.cs](tests/SsisAiRuntime.Tests/SqlDependencyGraphTests.cs) |
| SQL-03 | Stored procedure execution | Procedure dependency; do not guess transitive tables | Portable | Complete | [SqlDependencyAnalyzerTests.cs](tests/SsisAiRuntime.Tests/SqlDependencyAnalyzerTests.cs) |
| SQL-04 | CTE | Base-object dependencies; no physical-object edge to CTE alias | Portable | Complete | [SqlDependencyAnalyzerTests.cs](tests/SsisAiRuntime.Tests/SqlDependencyAnalyzerTests.cs) |
| SQL-05 | Temporary table | Explicit session-local/unsupported treatment; never promote it to a persistent schema object | Portable | Complete | [SqlDependencyAnalyzerTests.cs](tests/SsisAiRuntime.Tests/SqlDependencyAnalyzerTests.cs) |

For all SQL cases, preserve parsed evidence, connection scope, redaction, and negative behavior for dynamic or failed parsing. SQL analysis must not connect to a database to prove object existence.

SQL-01 through SQL-05 have portable test evidence. This foundation does not imply database catalog resolution or exhaustive SQL dialect coverage; analysis must remain connection-scoped, redacted, and offline.

#### Graph Acceptance Matrix v2: Future SQL Cases

Keep follow-on SQL cases separate from the completed foundation. Catalog classification requires an explicit credential-safe metadata provider; until then, do not connect to a database to resolve object existence.

| ID | Scenario | Expected behavior | Evidence class | Status |
| --- | --- | --- | --- | --- |
| SQL-06 | Three- and four-part names | Preserve qualification and connection scope | Portable | Deferred |
| SQL-07 | Synonyms | Leave unresolved without authoritative catalog metadata | Portable + optional metadata provider | Deferred |
| SQL-08 | Dynamic SQL | Emit a gap; never guess object edges | Portable | Partial |
| SQL-09 | SELECT INTO | Distinguish source reads from target creation/write | Portable | Deferred |
| SQL-10 | Table variables | Keep local objects distinct from persistent schema objects | Portable | Deferred |
| SQL-11 | Quoted identifiers containing periods | Preserve identifier boundaries | Portable | Deferred |
| SQL-12 | Same object name on two connections | Keep separate graph identities | Portable | Deferred |
| SQL-13 | Execute SQL parameter bindings | Link variables separately from SQL object dependencies | Native + parsed | Deferred |
| SQL-14 | Parse failure | Preserve owner and connection evidence with an explicit gap | Portable | Partial |

Existing dynamic-SQL and parse-failure tests establish basic negative behavior; SQL-08 and SQL-14 remain partial until their owner, connection, and graph-coverage assertions are explicit.

Add new project, Execute Package, and Data Flow acceptance cases to v2 rather than extending the frozen v1 lists. Candidate additions include loaded-project parameter discovery and cross-package parameter propagation, explicit distinctions among project/file/SQL Server/dynamic package references, cycle-path and bounded traversal assertions, and detailed multi-input provenance cases. The project fixture should be the third synthetic semantic baseline; a separate multi-input lineage fixture should be the fourth. After these are stable, prioritize sanitized real packages over additional synthetic-only graph tests.

### Toolbox Coverage Inventory

Do not promise bespoke semantic or mutation support for every Toolbox item. Build an extensible, per-environment coverage inventory from the existing SSIS 16 reference catalog plus native installed-registration discovery. Keep pipeline component registrations and `Application.TaskInfos` task registrations as distinct discovery sources; do not infer one inventory from the other. Record SSIS version, host/installation provenance, Microsoft built-in versus optional/vendor/unknown disposition, and exact creation name/CLSID. The present `ssis16.json` is a curated reference set, not the universal Toolbox; installed counts vary by host.

| Level | Disposition | Minimum evidence |
| --- | --- | --- |
| L0 Discovered | Registration or TaskInfo is present in the named runtime inventory | Native identity, creation name/CLSID, name, component/task kind, source host/version |
| L1 Structural | Package projection preserves topology and readable shape | Inputs, outputs, paths, connections, columns, containment, and exact native identities |
| L2 Semantic | Specific behavior is interpreted without conflating evidence sources | Named native/parsed/heuristic evidence and explicit unsupported semantics |
| L3 Editable | An operation-specific mutation exists | Typed preview/apply, lifecycle verification, limitations, and refusal coverage |
| L4 Executable Fixture | A synthetic fixture executed successfully under an explicit developer test | Recipe, prerequisites, assertions, redaction boundary, and isolated opt-in execution |

Discovery does not imply semantic support; semantics do not imply editability; editability does not imply execution verification. Current registration discovery is host-specific and the component catalog has no complete coverage level for every entry. The default Data Flow inspector preserves readable component identity/topology, inputs, outputs, paths, runtime connections, and columns, while component-specific semantic support is selective. Add explicit unsupported-semantic coverage for unknown/third-party components; preserve their structural metadata when readable instead of guessing behavior. Keep the 59-registration count as a measurement from the tested host, never as the universal SSIS Toolbox size. Pipeline and task discovery, and all L0-L4 levels, need separate inventory assertions.

Current maturity by surface:

| Surface | Highest established level | Remaining boundary |
| --- | --- | --- |
| Installed pipeline registrations from `FlowRunner components` | L0 | Per-registration maturity is not yet reported; list is local to the installed host |
| `catalog/components/ssis16.json` definitions | L0 reference metadata | Curated set, not a complete Toolbox snapshot or universal installation list |
| Generic Data Flow package projection | L1 when native metadata is readable | Per-component L2 semantics are selective; unknown/custom behavior remains unsupported |
| Specialized Derived Column, Data Conversion, SQL, and expression analysis | Selective L2 | Evidence is operation/component-specific and is not universal semantic coverage |
| Typed TaskHost/Transfer/selected Data Flow mutations | Selective L3 | Only advertised operations and their lifecycle cases are editable |
| Synthetic FlowRunner recipes | L4 for their named fixtures | Four selected component registrations have recipes; no general package execution certification |
| `Application.TaskInfos` tasks | Discovery used in native fixtures only | No general task inventory or L0-L4 report is exposed |

Unknown and third-party components should remain visible at L0/L1 when native identity and topology are readable, with explicit unsupported semantic coverage; they must not be silently promoted to L2-L4 based on catalog similarity.

## Next Independent Mutation Slices

- Choose one bounded task-description or simple Success/Failure/Completion precedence-constraint operation. For constraints, prove the exact ControlFlowGraph delta and preservation of unrelated tasks, constraints, identities, containment, and layout before adding expression-bearing constraints.
- Add parameter discovery and then narrow parameter edits after target identity, project context, and sensitivity boundaries are explicit.
- Add safe connection metadata edits only for allowlisted, non-secret fields; keep credentials and connection strings out of previews, results, logs, and corpus snapshots.
- Continue task/component edits as typed operations with exact target identity, operation-specific limitations, and lifecycle evidence. Keep discovery separate from writability; do not broaden a universal property setter.
- Keep nested-scope variable discovery read-only; nested-scope mutation remains package-scope-only until parent/child ownership and shadowing are proven for each mutation path. Discovery coverage does not enable nested-scope mutation.

These slices have no dependency on SSISDB, SQL Agent, or package generation. They may be prototyped independently while the current hardening milestone proceeds.

## Future Platform Expansion

### Package and Project Generation

Treat `create_package` and `create_project` as separate future operations. Package generation depends on useful primitives for task creation, simple constraints, variables, parameters, connection creation/configuration, basic Data Flow construction, and layout. Project creation is a later bootstrap capability composed over those primitives. Both use the common lifecycle with an explicit creation contract rather than inventing a source hash.

### Deployment and Scheduling Subsystem

Keep SSISDB deployment and SQL Server Agent management outside the package-editing milestone. If pursued, create a separate `SsisAiRuntime.Deployment` subsystem with explicit target profiles such as Test DB and production, inspect-first plans, immutable deployment receipts, and idempotent operations. A production mutation must name the server, catalog, folder, project, and environment explicitly; credentials must come from trusted configuration or a secret store. SQL Agent job management is separate from project deployment and requires its own inspection, authorization, idempotency, and verification contracts. External infrastructure is not a prerequisite for package-editing promotion.

### Additional Runtime

Add SSIS 15 as an isolated adapter/reference set after separate assembly-resolution, bitness, and native-fixture verification. Keep Core and mutation contracts runtime-neutral; do not claim side-by-side loading without isolation evidence.

## Boundaries and Verification

- Native SSIS owns authoritative loading, mutation, saving, and validation; DTSX XML is supplemental read-only evidence, not the normal mutation path.
- Keep package execution opt-in and separate from mutation validation. Keep SSISDB deployment and SQL Agent work out of package-editing release criteria.
- Build and portable/native test commands are in [BuildPackage.md](BuildPackage.md). The provider-neutral runner is [eng/ci/Invoke-Ssis16MutationGate.ps1](eng/ci/Invoke-Ssis16MutationGate.ps1).
- Corpus fixtures and reviewed snapshots are in [tests/LayoutFixtures](tests/LayoutFixtures) and [tests/LayoutFixtures/CorpusBaselines](tests/LayoutFixtures/CorpusBaselines).
- Record dated verification outcomes in [HISTORY.md](HISTORY.md); use [STATUS.md](STATUS.md) for present maturity and evidence.
