# Status

As of 2026-11-05. This is the current evidence ledger; dated details are in [HISTORY.md](HISTORY.md), and ordered future work is in [PLAN.md](PLAN.md).

## Maturity Matrix

`Yes` means checked-in tests or configuration directly demonstrate the claim. `Partial` means some applicable behavior is tested but operation-specific lifecycle/failure coverage remains. `Pending` means evidence is absent. `Supported` is an explicit release claim, not a synonym for implemented. Corpus and Required CI are `Yes` only with operation-specific approved baseline coverage and provider-required status configuration, respectively.

| Operation | Prototype | Native fixture | Lifecycle | Corpus | Required CI | Supported | Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `rename_task` | Yes | Yes | Yes | Pending | Pending | No | [MutationHost lifecycle](tests/SsisAiRuntime.Ssis16IntegrationTests/MutationHostLifecycleTests.cs#L33), [native CLI apply and invariants](tests/SsisAiRuntime.Ssis16IntegrationTests/MutationHostLifecycleTests.cs#L470) |
| `insert_derived_column` | Yes | Yes | Partial | Pending | Pending | No | [path-splice fixtures](tests/SsisAiRuntime.Ssis16IntegrationTests/DataFlowPathSpliceFixtureTests.cs), [host insertion lifecycle](tests/SsisAiRuntime.Ssis16IntegrationTests/MutationHostLifecycleTests.cs) |
| `insert_data_conversion` | Yes | Yes | Partial | Pending | Pending | No | [path-splice fixtures](tests/SsisAiRuntime.Ssis16IntegrationTests/DataFlowPathSpliceFixtureTests.cs), [host insertion lifecycle](tests/SsisAiRuntime.Ssis16IntegrationTests/MutationHostLifecycleTests.cs) |
| `update_transfer_tables` | Yes | Yes | Yes | Pending | Pending | No | [native task round-trip and modes](tests/SsisAiRuntime.Ssis16IntegrationTests/MutationHostLifecycleTests.cs#L274), [host lifecycle](tests/SsisAiRuntime.Ssis16IntegrationTests/MutationHostLifecycleTests.cs#L602) |
| `set_task_property` | Yes | Yes | Yes | Pending | Pending | No | [native CLI preview/apply, validation, reload, and invariants](tests/SsisAiRuntime.Ssis16IntegrationTests/MutationHostLifecycleTests.cs#L470) |
| `update_task_collection` | Yes | Yes | Partial | Pending | Pending | No | [native collection preview and typed edit cases](tests/SsisAiRuntime.Ssis16IntegrationTests/MutationHostLifecycleTests.cs#L274) |
| `set_variable_metadata` (Description only) | Yes | Yes | Yes | Pending | Pending | No | [variable discovery and edit fixture](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs#L376) |
| `set_variable_expression` | Yes | Yes | Yes | Pending | Pending | No | [variable discovery and edit fixture](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs#L376) |
| `set_variable_value` (environment-backed) | Yes | Yes | Yes | Pending | Pending | No | [variable discovery and edit fixture](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs#L376) |
| Column resize (separate workflow) | Yes | Yes | Partial | Pending | Pending | No | [15 resize lifecycle cases](tests/SsisAiRuntime.Ssis16IntegrationTests/ColumnResizeLifecycleTests.cs) |

The request name `set_variable_description` corresponds to the implemented `set_variable_metadata` operation, whose only writable field is `Description`. Variable mutations are package-scope-only. `set_variable_value` rejects direct JSON values and redacts its environment-variable name/value, but sensitivity remains `Unclassified`; it is not supported for secret writing.

## Read-Only Variable Inventory

`ai variable.properties` enumerates variables in inspectable package and nested executable scopes and returns both variable and scope native IDs. The native fixture asserts package and task-scope discovery. Scope-read or ownership failures are reported as incomplete coverage; discovery does not enable nested-scope mutation. Sensitivity remains `Unclassified`, and values and expression text remain omitted. Evidence: [VerifyVariableProperties](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs#L376) and [PackageVariableInspector](src/SsisAiRuntime.Ssis16/PackageVariableInspector.cs).

## Cross-Cutting Evidence

- The shared mutation lifecycle is implemented and operation requirements are exposed by `ai mutation.capabilities`; native assertions cover the lifecycle contract for every advertised operation and the variable-operation limitations. Evidence: [capability lifecycle assertions](tests/SsisAiRuntime.Ssis16IntegrationTests/MutationHostLifecycleTests.cs#L772).
- The native harness creates and loads an Execute Package Task and asserts `dependency.execute_package_target_unresolved`, task containment, and absence of guessed schema-object edges. Positive target resolution remains unimplemented. Evidence: [Execute Package target coverage probe](tests/SsisAiRuntime.Ssis16IntegrationTests/Program.cs).
- Exact-path insertion tests cover component-configuration rollback, refusal of nonpositive placement dimensions, and Union All duplicate-output preflight before mutation. Derived Column host tests inject save/reload failures and reject an unexpected package-identity delta using host/validator test doubles. Rollback after failures during/after native adapter refresh and corresponding Data Conversion failure cases remain unverified, so insertion remains `Lifecycle: Partial`. Evidence: [path-splice fixtures](tests/SsisAiRuntime.Ssis16IntegrationTests/DataFlowPathSpliceFixtureTests.cs), [designer layout probe](tests/SsisAiRuntime.Ssis16IntegrationTests/DesignerLayoutTests.cs), and [insertion host lifecycle](tests/SsisAiRuntime.Ssis16IntegrationTests/MutationHostLifecycleTests.cs#L665).
- `SSIS16-Mutation-Gate` is a provider-neutral script that runs the solution build, portable tests, nine corpus baseline checks, native harness, mutation lifecycle, designer/layout, resize, exact-path, and Union All stages. The full gate passed after the synthetic corpus additions: 202 runtime, 13 corpus, and 23 mutation tests; nine matching baselines; and all native stages. This local pass does not mean the provider status is configured as required.
- No CI workflow or provider branch-protection configuration is checked in. Therefore Required CI remains Pending for every operation even when local/native tests pass. Provider-side required-status configuration is documented in [PLAN.md](PLAN.md).
- The nine approved corpus baselines comprise seven structural layout fixtures and two native-generated synthetic semantic fixtures (`SyntheticScopedControlFlow` and `SyntheticSqlDependencies`). They exercise scope/precedence and parsed SQL-object shapes, but are not representative business packages. There are no operation-specific mutation baselines or sanitized representative business packages, so Corpus remains Pending throughout the matrix.
- No operation is currently marked Supported. `rename_task` is the first promotion candidate, subject to representative corpus evidence, required CI, and its explicit invariant matrix.

Native tests do not execute packages or connect to data sources. The known SSIS interop `CS8012` processor-mismatch warning is documented in the build history; it has passed on the installed x64 SSIS 16 host but does not certify other installations.