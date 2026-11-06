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
3. **Close the remaining graph evidence gaps.** Control Flow CF-01 through CF-12, Variable/Parameter VP-01 through VP-15, and SQL SQL-01 through SQL-05 have portable and/or native test evidence. Remaining priorities are project-inventory-backed Execute Package resolution and bindings, additional Execute Package target/cycle cases, and native Data Flow coverage for multi-input lineage, asynchronous transformations, Lookup, Aggregate, custom components, and exact-port failure attribution. The corpus still has seven layout fixtures and two native-generated synthetic semantic fixtures, not representative business packages; add sanitized representatives when available and review semantic diffs before approving baselines.
4. **Promote `rename_task` only after its remaining gates pass.** It is the strongest first candidate because its intended semantic delta is narrow. Preserve the existing task ID/count, hierarchy, connections, expressions, SQL dependencies, and unrelated properties. Require reviewed representative corpus evidence and required CI before marking it Supported.
5. **Harden exact-path insertion.** `DataFlowPathSpliceFixtureTests` proves that a component-configuration failure triggers rollback, restores the original endpoint topology, and removes the inserted component; a Union All duplicate-output conflict is refused before mutation. `DesignerLayoutTests` rejects nonpositive placement dimensions. Derived Column host tests now inject save/reload failure and an unexpected package-identity delta; these use host/validator test doubles. Remaining work: rollback after failures during/after native adapter refresh and corresponding failure cases for Data Conversion. Preserve nonselected paths, component metadata, and layout.
6. **Establish trusted sensitivity classification.** Until a verifiable policy distinguishes safe from sensitive targets, do not promote writes to sensitive or unclassified variables. Keep the environment-backed value prototype redacted and unsupported for secret writing.

The current corpus has seven structural layout fixtures and two synthetic semantic examples, not representative business packages. For `rename_task` promotion, target 3-5 sanitized packages from at least two projects, including nested control flow and a nontrivial Data Flow. For broader graph confidence, target 8-12 representative packages and review every unresolved item in each approved baseline. These are engineering targets, not universal standards or prerequisites for independent prototypes. Add sanitized artifacts only when available; review semantic diffs before explicitly approving baselines, and never present synthetic fixtures as business coverage.

## Graph Acceptance Matrix

This matrix is the acceptance contract for expanding dependency and control-flow graphs. Each fixture records expected nodes, edge direction and kind, evidence class, implementation status, impact-query behavior, and negative behavior. A package snapshot may have unrelated coverage gaps; assert completeness for the tested slice rather than treating global `IsComplete` as the only result.

Use only these row statuses: **Complete**, **Partial**, **Open**, **Blocked by fixture**, **Deferred**, and **Not supported**. `Complete` means the row's stated assertions have evidence; it does not imply support beyond that fixture or evidence class. `Partial` means only part of the row is asserted. `Open` means no adequate assertion exists. `Blocked by fixture` means the capability cannot be tested until an identified fixture/runtime dependency is available. `Deferred` is intentionally out of the current milestone. `Not supported` is an explicit product boundary.

Keep evidence layers distinct:

| Layer | Authority | Examples |
| --- | --- | --- |
| Structural | Native SSIS object model | containment, precedence constraints, Data Flow paths |
| Declared dependency | Native metadata or explicit binding | parameter binding, connection assignment, static Execute Package target |
| Parsed dependency | ScriptDom or expression parser plus scope resolution | SQL objects, parsed expression references |
| Heuristic dependency | Lexical candidate plus bounded scope resolution | variable/parameter references without authoritative native binding |

Do not flatten these relationships into a generic `DependsOn` edge. Preserve current owner-to-dependency orientation; answer “what consumes this?” through incoming/impact queries instead of adding duplicate reverse edges. For every positive edge fixture, assert direct and recursive impact/query results and that the expected path is retained. For ambiguous, missing, or dynamic targets, assert no guessed edge, explicit candidates or coverage, and no fabricated impact path. Expression-derived variable/parameter edges remain heuristic and never replace native column-lineage evidence.

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
| Discover project parameters from a loaded `.dtproj` or equivalent project inventory | None | Open | No project-loading fixture or inventory provider |
| Resolve package references against actual project inventory | None | Open | No authoritative project package inventory |

VP-08 proves graph construction from injected host metadata; it does not prove `.dtproj` discovery or project loading. Add parser cases for `@ParserFlag`, `@[User::ParserFlag]`, `@[$Package::ProbeParameter]`, and `@[$Project::ProjectParameter]`; also casts, conditional operators, function arguments, concatenation, nested parentheses, string literals resembling references, and comments/escaped strings where supported. SSIS expressions may be owned by variables, constraints, For Loops, task/connection properties, Conditional Split, Derived Column, Execute SQL properties, and Execute Package targets. `@` references and `#` Data Flow lineage identifiers are distinct token classes; `#138` must never become a variable candidate.

Add parser cases for `@ParserFlag`, `@[User::ParserFlag]`, `@[$Package::ProbeParameter]`, and `@[$Project::ProjectParameter]`; also casts, conditional operators, function arguments, concatenation, nested parentheses, string literals resembling references, and comments/escaped strings where supported. SSIS expressions may be owned by variables, constraints, For Loops, task/connection properties, Conditional Split, Derived Column, Execute SQL properties, and Execute Package targets. `@` references and `#` Data Flow lineage identifiers are distinct token classes; `#138` must never become a variable candidate.

### C. Execute Package Relationships

An Execute Package invocation is not containment or precedence. Add a distinct `InvokesPackage` relationship only when the target evidence supports it. Keep child parameter bindings separate from invocation edges.

| ID | Fixture | Required assertions | Evidence class | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| EP-01 | Project-reference child present | Resolve exact child package and emit `InvokesPackage` only from authoritative inventory | Native + project inventory | Open | No project inventory fixture |
| EP-02 | Project-reference target absent | Preserve declared name; unresolved-target gap | Native + portable | Complete | [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs); [DependencyAndPlanningTests.cs](tests/SsisAiRuntime.Tests/DependencyAndPlanningTests.cs) |
| EP-03 | Duplicate candidate package names | No guessed edge; return candidate identities | Project inventory | Open | No inventory ambiguity fixture |
| EP-04 | Literal file-system package path | External reference node with normalized evidence; do not imply loaded child | Native | Open | No file-reference fixture |
| EP-05 | File path supplied by expression | Link expression inputs; target remains dynamic | Native + heuristic | Open | No dynamic target graph assertion |
| EP-06 | SQL Server package reference | External catalog/storage reference node | Native | Open | No SQL Server reference fixture |
| EP-07 | Child package also in corpus | Optionally correlate corpus identity without claiming same-session load evidence | Corpus | Deferred | Corpus identity correlation is outside the current graph slice |
| EP-08 | Self-reference | Cycle diagnostic; no recursive load | Project inventory | Open | No cycle fixture |
| EP-09 | A invokes B, B invokes C | Bounded transitive impact traverses package calls | Project graph | Open | No package traversal implementation |
| EP-10 | A invokes B, B invokes A | Cycle-safe traversal and explicit cycle evidence | Project graph | Open | No cycle fixture |
| EP-11 | Multiple parents invoke one child | Preserve every incoming invocation edge | Project inventory | Open | No resolved-child inventory fixture |
| EP-12 | Child parameter bindings | Separate binding edge with invoking task, child parameter, source symbol, target package, and evidence | Native + project inventory | Open | No binding fixture or graph model |
| EP-13 | Disabled Execute Package task | Preserve invocation topology and disabled state | Native | Open | No disabled-task invocation assertion |
| EP-14 | Statically unresolved target expression | Dynamic-target gap and links to controlling variables/parameters | Native + heuristic | Open | No dynamic target graph assertion |

EP-02 is the only complete Execute Package matrix row. The current native behavior preserves a declared Project Reference as an `ExternalPackageReference` node and `InvokesPackage` edge with `DeclaredProjectReference` evidence, while retaining an unresolved-target gap because project inventory is absent. See [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs) and [DependencyAndPlanningTests.cs](tests/SsisAiRuntime.Tests/DependencyAndPlanningTests.cs).

### Next Graph Milestone: Project-Aware Graph Closure

Introduce an explicit project inventory abstraction (for example, `IProjectPackageInventory`) that supplies package identities and project parameter identities without implicit file or network discovery. Package identity should retain project identity, project-relative package name, package identity when available, deployment model, and reference type. Resolve only a unique authoritative match; preserve missing and ambiguous candidates without guessing.

Implementation order:

1. EP-01: uniquely resolve a Project Reference against a supplied inventory.
2. EP-03: reject duplicate or ambiguous inventory matches.
3. EP-12: model child parameter bindings separately from invocation edges.
4. EP-08 and EP-10: detect self-reference and cycles without recursive loading.
5. EP-09: add deterministic, bounded transitive package-impact traversal.
6. EP-05 and EP-14: connect dynamic target expressions to their controlling symbols while keeping the target unresolved.
7. EP-04 and EP-06: model file-system and SQL Server references after project-reference semantics are stable.

The acceptance fixture should contain `Project.params`, `Parent.dtsx`, and `Child.dtsx`; a project parameter used by both packages; a child package parameter; a parent Execute Package task with a binding from a parent variable or parameter; and a deliberately missing child reference. Use it for VP-08, EP-01, EP-02, and EP-12, while retaining separate fixtures for ambiguity and cycles. Add deterministic project graph snapshots and expose project graph coverage through CLI and AI results. Keep child artifact loading explicit and bounded; ordinary package inspection must not recursively load packages. No package execution or source-artifact mutation is permitted, and all values and expressions remain redacted.

### D. Expression-Owner Coverage

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

### E. Data-Flow and Column-Lineage Graph

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

1. **Multi-input provenance:** Union All, Merge, and Merge Join. Keep input ports distinct; do not merge mappings by column name; test duplicate names, upstream traversal across all contributors, and bounded downstream traversal.
2. **Asynchronous lineage boundaries:** Aggregate and other transformations that create new buffers (include Sort when the fixture environment supports it). Recognize new lineage IDs without labeling derivation as identity; use qualified relationships when semantics are incomplete.
3. **Lookup semantics:** Separate pass-through, reference-derived, no-match, and error outputs. Do not claim table-level lineage unless connection and query metadata independently support it.
4. **Exact failure attribution:** Attach each gap to Data Flow task, component native ID/name, input or output port ID, lineage ID when available, failure code, and evidence source.

Native Conditional Split coverage and a native custom-component topology fixture also remain open. See [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs), [DataFlowOverviewTests.cs](tests/SsisAiRuntime.Tests/DataFlowOverviewTests.cs), and [UnionAllMetadataProbe.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/UnionAllMetadataProbe.cs).

### F. SQL Dependency Graph

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

#### Future SQL Matrix

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
