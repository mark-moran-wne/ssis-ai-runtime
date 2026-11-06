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
3. **Complete graph and corpus evidence.** Extend exact edge assertions for package/project parameters and indirect expression references; resolve Execute Package targets only when native metadata proves the relationship, and retain `dependency.execute_package_target_unresolved` otherwise. A native negative-case fixture now confirms no target is guessed. The corpus has seven layout fixtures and two native-generated synthetic semantic fixtures; none are representative business packages. Add sanitized representatives when available and review semantic diffs before approving baselines.
4. **Promote `rename_task` only after its remaining gates pass.** It is the strongest first candidate because its intended semantic delta is narrow. Preserve the existing task ID/count, hierarchy, connections, expressions, SQL dependencies, and unrelated properties. Require reviewed representative corpus evidence and required CI before marking it Supported.
5. **Harden exact-path insertion.** `DataFlowPathSpliceFixtureTests` proves that a component-configuration failure triggers rollback, restores the original endpoint topology, and removes the inserted component; a Union All duplicate-output conflict is refused before mutation. `DesignerLayoutTests` rejects nonpositive placement dimensions. Derived Column host tests now inject save/reload failure and an unexpected package-identity delta; these use host/validator test doubles. Remaining work: rollback after failures during/after native adapter refresh and corresponding failure cases for Data Conversion. Preserve nonselected paths, component metadata, and layout.
6. **Establish trusted sensitivity classification.** Until a verifiable policy distinguishes safe from sensitive targets, do not promote writes to sensitive or unclassified variables. Keep the environment-backed value prototype redacted and unsupported for secret writing.

The current corpus has seven structural layout fixtures and two synthetic semantic examples, not representative business packages. Add sanitized representative packages only when suitable artifacts are available; review semantic diffs before explicitly approving baselines. Do not present synthetic fixtures as business coverage or use corpus gaps to block unrelated prototype work.

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
