# SSIS AI Runtime History

This document records completed milestones, investigation evidence, and dated verification results. For current behavior and safety limits, see [README.md](README.md). For remaining work and design decisions, see [Plan.md](Plan.md). Build and verification commands are in [BuildPackage.md](BuildPackage.md).

## 2026-10-05 Verification

- Full portable suite: **150 passed, 0 failed, 0 skipped**. This is the latest recorded verification result, not a permanently fixed suite size.
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