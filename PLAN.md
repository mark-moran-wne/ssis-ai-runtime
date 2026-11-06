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
3. **Complete graph and corpus evidence.** Work through the acceptance matrix below for package/project parameters, indirect expression references, and Execute Package relationships. Resolve targets only when native metadata proves the relationship; otherwise retain explicit unresolved coverage. The corpus has seven layout fixtures and two native-generated synthetic semantic fixtures; none are representative business packages. Add sanitized representatives when available and review semantic diffs before approving baselines.
4. **Promote `rename_task` only after its remaining gates pass.** It is the strongest first candidate because its intended semantic delta is narrow. Preserve the existing task ID/count, hierarchy, connections, expressions, SQL dependencies, and unrelated properties. Require reviewed representative corpus evidence and required CI before marking it Supported.
5. **Harden exact-path insertion.** `DataFlowPathSpliceFixtureTests` proves that a component-configuration failure triggers rollback, restores the original endpoint topology, and removes the inserted component; a Union All duplicate-output conflict is refused before mutation. `DesignerLayoutTests` rejects nonpositive placement dimensions. Derived Column host tests now inject save/reload failure and an unexpected package-identity delta; these use host/validator test doubles. Remaining work: rollback after failures during/after native adapter refresh and corresponding failure cases for Data Conversion. Preserve nonselected paths, component metadata, and layout.
6. **Establish trusted sensitivity classification.** Until a verifiable policy distinguishes safe from sensitive targets, do not promote writes to sensitive or unclassified variables. Keep the environment-backed value prototype redacted and unsupported for secret writing.

The current corpus has seven structural layout fixtures and two synthetic semantic examples, not representative business packages. Add sanitized representative packages only when suitable artifacts are available; review semantic diffs before explicitly approving baselines. Do not present synthetic fixtures as business coverage or use corpus gaps to block unrelated prototype work.

## Graph Acceptance Matrix

This matrix is the acceptance contract for expanding dependency and control-flow graphs. Each fixture records expected nodes, edge direction and kind, evidence, local completeness/coverage, impact-query behavior, and negative behavior. A package snapshot may have unrelated coverage gaps; assert completeness for the tested slice rather than treating global `IsComplete` as the only result.

Keep evidence layers distinct:

| Layer | Authority | Examples |
| --- | --- | --- |
| Structural | Native SSIS object model | containment, precedence constraints, Data Flow paths |
| Declared dependency | Native metadata or explicit binding | parameter binding, connection assignment, static Execute Package target |
| Parsed dependency | ScriptDom or expression parser plus scope resolution | SQL objects, parsed expression references |
| Heuristic dependency | Lexical candidate plus bounded scope resolution | variable/parameter references without authoritative native binding |

Do not flatten these relationships into a generic `DependsOn` edge. Preserve current owner-to-dependency orientation; answer “what consumes this?” through incoming/impact queries instead of adding duplicate reverse edges. For every positive edge fixture, assert direct and recursive impact/query results and that the expected path is retained. For ambiguous, missing, or dynamic targets, assert no guessed edge, explicit candidates or coverage, and no fabricated impact path. Expression-derived variable/parameter edges remain heuristic and never replace native column-lineage evidence.

### A. Control-Flow Structure

| ID | Fixture | Required assertions | Evidence / state |
| --- | --- | --- | --- |
| CF-01 | Two unconnected package tasks | Two task nodes and package containment edges; no execution-order edge | Native / Complete for structure |
| CF-02 | Linear Success constraint | Exact endpoints, `DTSExecResult.Success`, one precedence edge | Native / Complete |
| CF-03 | Failure constraint | Exact endpoints and Failure result | Native / Complete |
| CF-04 | Completion constraint | Exact endpoints and Completion result | Native / Complete |
| CF-05 | Expression-only constraint | Preserve `EvalOp=Expression`; emit expression dependencies separately | Native + parsed / Complete when references resolve |
| CF-06 | Expression-and-constraint | Preserve `ExpressionAndConstraint`, execution result, references, and AND semantics | Native + parsed / Complete or explicit parse gap |
| CF-07 | Expression-or-constraint | Preserve `ExpressionOrConstraint`; never report logical AND | Native + parsed / Complete or explicit parse gap |
| CF-08 | Multiple incoming constraints | Preserve each predecessor and `LogicalAnd`; do not collapse fan-in | Native / Complete |
| CF-09 | Sequence container | Package contains Sequence; Sequence contains tasks; internal edges stay scoped | Native / Complete |
| CF-10 | Nested Foreach and Sequence | Correct multi-level containment and nearest-scope resolution | Native + heuristic / Complete or scoped gap |
| CF-11 | Event-handler flow | Separate handler scope with its own executables and constraints | Native / Complete |
| CF-12 | Missing endpoint | Gap is attached to the unresolved endpoint; never bind by display name | Native / Incomplete |

Assert the native constraint properties (`Value`, `EvalOp`, `Expression`, and `LogicalAnd`) directly. Do not infer semantics from designer connector color or labels.

Current evidence: all CF-01 through CF-11 rows are asserted in the native harness, including unconnected tasks, Success/Failure/Completion, expression operators, Sequence fan-in, nested Foreach/Sequence scope, and event-handler-local precedence. CF-12 missing-endpoint refusal is asserted in the portable graph tests. Section A is covered; continue the remaining graph layers below. Evidence: [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs#L391) and [ControlFlowGraphBuilderTests.cs](tests/SsisAiRuntime.Tests/ControlFlowGraphBuilderTests.cs).

### B. Variable and Parameter Dependencies

Variable scope is package/container/task/event-handler based. Parent declarations are visible downward; task declarations are not visible outside their owner. Names are case-sensitive unless a native behavior test proves otherwise. Package and project parameters are distinct node kinds.

| ID | Fixture | Required assertions | Resolution |
| --- | --- | --- | --- |
| VP-01 | Package variable in task property expression | One owner-to-variable dependency with exact owner/declaration IDs | Resolved |
| VP-02 | Container variable in nested task | Bind to nearest visible declaration | Resolved |
| VP-03 | Same name at package and container scopes | Nested owner binds to container declaration | Resolved by shadowing |
| VP-04 | Same-name sibling-scope variables | No cross-sibling edge | Unresolved outside scope |
| VP-05 | Task variable referenced outside task | No edge; retain unresolved/out-of-scope evidence | Out of scope |
| VP-06 | System variable reference | Typed system-variable node and dependency | Resolved |
| VP-07 | Package parameter reference | Typed package-parameter node, not variable node | Resolved |
| VP-08 | Project parameter with project metadata | Typed project-parameter node | Resolved |
| VP-09 | Project parameter without project metadata | Candidate retained; no target edge; coverage gap | Incomplete |
| VP-10 | Mixed variable/parameter expression | Independent edge for each resolved reference | Resolved or partial |
| VP-11 | Ambiguous unqualified reference | No guessed edge; candidates listed | Ambiguous |
| VP-12 | Case mismatch | No match unless native behavior proves case-insensitive binding | Unresolved |
| VP-13 | Sensitive parameter | Retain dependency; redact value and default | Resolved/redacted |
| VP-14 | Expression-owned variable references another symbol | Link the referenced symbol to the variable-expression owner without raw text | Resolved |
| VP-15 | Chained variable expressions | Recursive impact reaches indirect consumers without adding a direct edge | Resolved |

Current evidence: VP-01 through VP-15 have portable and/or native fixture assertions. They cover package/container/task/sibling/system scope, package and explicitly supplied project parameters, mixed references, ambiguity, case mismatch, sensitive-parameter redaction, variable-expression ownership, and indirect impact without shortcut edges. VP-08 exercises the hosting API with explicit project metadata; it does not claim loading or validating a real `.dtproj`. Project-package integration and additional parser/owner combinations remain open.

Add parser cases for `@ParserFlag`, `@[User::ParserFlag]`, `@[$Package::ProbeParameter]`, and `@[$Project::ProjectParameter]`; also casts, conditional operators, function arguments, concatenation, nested parentheses, string literals resembling references, and comments/escaped strings where supported. SSIS expressions may be owned by variables, constraints, For Loops, task/connection properties, Conditional Split, Derived Column, Execute SQL properties, and Execute Package targets. `@` references and `#` Data Flow lineage identifiers are distinct token classes; `#138` must never become a variable candidate.

### C. Execute Package Relationships

An Execute Package invocation is not containment or precedence. Add a distinct `InvokesPackage` relationship only when the target evidence supports it. Keep child parameter bindings separate from invocation edges.

| ID | Fixture | Required assertions | Completeness |
| --- | --- | --- | --- |
| EP-01 | Project-reference child present | Exact child package and `InvokesPackage` edge | Pending: requires explicit project package inventory |
| EP-02 | Project-reference target absent | Preserve declared name; unresolved-target gap | Covered: declared external edge plus unresolved-target coverage |
| EP-03 | Duplicate candidate package names | No guessed edge; return candidate identities | Ambiguous |
| EP-04 | Literal file-system package path | External reference node with normalized evidence; do not imply loaded child | Declared |
| EP-05 | File path supplied by expression | Link expression inputs; target remains dynamic | Partial |
| EP-06 | SQL Server package reference | External catalog/storage reference node | Declared |
| EP-07 | Child package also in corpus | Optionally correlate corpus identity without claiming same-session load evidence | Corpus-resolved |
| EP-08 | Self-reference | Cycle diagnostic; no recursive load | Resolved with warning |
| EP-09 | A invokes B, B invokes C | Bounded transitive impact traverses package calls | Resolved |
| EP-10 | A invokes B, B invokes A | Cycle-safe traversal and explicit cycle evidence | Resolved with cycle |
| EP-11 | Multiple parents invoke one child | Preserve every incoming invocation edge | Complete |
| EP-12 | Child parameter bindings | Separate binding edge with invoking task, child parameter, source symbol, target package, and evidence | Declared/resolved |
| EP-13 | Disabled Execute Package task | Preserve invocation topology and disabled state | Complete |
| EP-14 | Statically unresolved target expression | Dynamic-target gap and links to controlling variables/parameters | Partial |

Current evidence: EP-02 is covered natively: a declared Project Reference produces an `ExternalPackageReference` node and `InvokesPackage` edge with `DeclaredProjectReference` evidence, plus an unresolved-target gap because no project package inventory is supplied. EP-01 positive resolution and the remaining EP rows are open. See [VerifyExecutePackageTargetCoverage](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs#L345) and [DependencyAndPlanningTests.cs](tests/SsisAiRuntime.Tests/DependencyAndPlanningTests.cs).

Proposed distinct nodes include `Package`, `ExternalPackageReference`, and `ExecutePackageTask`; proposed edges include `InvokesPackage` and `BindsChildParameter`. Do not recurse into child packages unless a caller explicitly supplies a bounded package set and depth/result limits.

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

| ID | Fixture | Required assertions |
| --- | --- | --- |
| DF-01 | Source to destination | Exact path and observed input/output column identities |
| DF-02 | Derived Column creates output | New lineage and source-column dependencies |
| DF-03 | Derived Column replaces output | Preserve replacement semantics and original-source dependency |
| DF-04 | Conditional Split | One input lineage may feed multiple conditional outputs |
| DF-05 | Data Conversion | Output traces to source with conversion relationship |
| DF-06 | Union All | Multiple selected inputs map to one output; retain per-input mappings |
| DF-07 | Merge/Merge Join | Preserve separate left/right provenance |
| DF-08 | Lookup | Separate pass-through, lookup-derived, and error-output lineage |
| DF-09 | Aggregate | Do not misreport aggregate derivation as identity mapping |
| DF-10 | Asynchronous transformation | New lineage IDs and explicit boundary |
| DF-11 | Unattached output | Preserve metadata; do not invent downstream path |
| DF-12 | Missing upstream metadata | Gap tied to exact component and port |
| DF-13 | Custom component | Preserve native identity/topology; classify semantics unsupported |
| DF-14 | Expression uses variable and columns | Native column edges plus separately classified heuristic variable edge |

Every case must assert that heuristic enrichment neither upgrades nor replaces native column observations.

Current evidence: DF-01/DF-05 are covered by the native source-to-Data-Conversion fixture and bidirectional explicit-mapping traces; DF-02/DF-03/DF-14 by the native Derived Column parser fixture and separate heuristic variable edge; DF-04 by a portable Conditional Split-shaped fan-out fixture; DF-10 by the asynchronous-output negative test; DF-11 by an unattached-output test; and DF-12/DF-13 have only partial model-level evidence for dangling synchronous metadata and opaque transformations. Native Conditional Split coverage, Union All per-input lineage, Merge/Merge Join, Lookup, Aggregate, exact component/port attribution for missing metadata, and a native custom-component topology fixture remain open. See [Program.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs), [DataFlowOverviewTests.cs](tests/SsisAiRuntime.Tests/DataFlowOverviewTests.cs), and [UnionAllMetadataProbe.cs](tests/SsisAiRuntime.Ssis16IntegrationTests/UnionAllMetadataProbe.cs).

### F. SQL Dependency Graph

Continue connection-scoped ScriptDom evidence with these fixtures:

| ID | Fixture | Expected result |
| --- | --- | --- |
| SQL-01 | SELECT from two-part name | Read dependency with connection scope |
| SQL-02 | INSERT, UPDATE, DELETE, MERGE | Correct read/write classification |
| SQL-03 | Stored procedure execution | Procedure dependency; do not guess transitive tables |
| SQL-04 | CTE | Base-object dependencies; no physical-object edge to CTE alias |
| SQL-05 | Temporary table | Explicit session-local/unsupported treatment; never promote it to a persistent schema object |

For all SQL cases, preserve parsed evidence, connection scope, redaction, and negative behavior for dynamic or failed parsing. SQL analysis must not connect to a database to prove object existence.

Current evidence: SQL-01/SQL-03 are covered by the analyzer and connection-scoped graph tests; SQL-02 now covers INSERT/UPDATE/DELETE/MERGE read/write classification; SQL-04/SQL-05 cover statement-scoped CTEs and ensure temporary tables are not projected as persistent objects. Dynamic SQL and parse-failure negative cases remain explicit. Evidence: [SqlDependencyAnalyzerTests](tests/SsisAiRuntime.Tests/SqlDependencyAnalyzerTests.cs) and [SqlDependencyGraphTests](tests/SsisAiRuntime.Tests/SqlDependencyGraphTests.cs).

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
