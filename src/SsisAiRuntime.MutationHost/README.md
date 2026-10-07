# Mutation Host Prototype

This .NET Framework 4.8 x64 library composes native SSIS loading, mutation previews, corpus fingerprints, checkpoint and validation callbacks, and staging for task renaming and bounded coordinated column widening. The supplied prototype has been repaired using native lifecycle regression tests. It is not wired into the inspection CLI or developer FlowRunner, and provides no executable entry point or production checkpoint/validation service.

## Composition Boundaries

- The host references Core, Inspectors, AI, Corpus, Mutations, and Ssis16. No portable project depends on the host. Deployment requires Windows x64, .NET Framework 4.8, and the installed SSIS 16 runtime; the library does not package the native runtime.
- `IMutationCheckpointService` is injected. Storage, compression, retention, restore layout, and protection of checkpoint contents belong to its implementation. A checkpoint must preserve the full DTSX artifact; returning a fingerprint or hash alone does not establish that restoration is possible.
- `INativeMutationValidator` is injected. Its implementation must explicitly define available checks and resource access and return safe diagnostic codes. Successful loading is not native validation, and a stub reporting success is not verification that the required validation occurred.
- `MutationHostResult` is a distinct lifecycle-evidence contract, not the two-field `MutationResult`. The existing `MutationContractSerializer` does not serialize it; a dedicated, versioned host-result serializer remains follow-up work.
- The host rebuilds the rename preview or checks the exact widening plan against the loaded native artifact, saves with native `SaveToXML`, and reloads the staged XML before validation and semantic verification. It depends on injected implementations to provide truthful checkpoint and validation evidence.

## Coordinated Column Widening

`PreviewColumnWidening(sourcePath, taskId, sourceComponentId, outputColumnId, destinationComponentId, proposedWidth)` returns a source-hash-bound plan and the current/proposed width at each affected layer. Execution uses `MutationHostRequest` with that plan and a distinct destination path. Selectors are native IDs, not names. The supported topology is exactly one direct Flat File Source output connected to a Flat File Destination or OLE DB Destination input; types must be compatible `DT_STR` or `DT_WSTR`. Flat File metadata must be delimited, and width is bounded to 1..4000. The operation refuses shrink/no-op plans, changed source bytes or widths, unsupported branches, missing IDs, incompatible metadata, and existing destinations.

The edit coordinates source file-manager, output and external metadata, the selected destination input and external metadata, and the destination file-manager width when present. After native save/reload and the validator callback, verification checks the exact resulting native signature and proposed widths. The signature ignores only the package-root `VersionGUID`, which SSIS regenerates during serialization. Corpus snapshots do not contain column widths, so this native signature is required in addition to the projected semantic checks. Tests independently assert that six intended width attributes change and that a neighboring column, selected input identity, and mapping are preserved.

This creates a separate DTSX copy; it never overwrites the source or issues physical database DDL. The lifecycle fixture uses injected test checkpoint and metadata-only validator implementations and does not call SSIS `Validate` or `Execute`. No production checkpoint/validator, editing CLI, or broad mutation authorization is supplied.

The host holds the source read-only from hashing/loading through checkpoint creation and publication. The stager requires a different destination, refuses an existing destination, and opens another source read lock while staging. Both original and reloaded native packages are disposed, including load failures. Source file sharing prevents normal Windows writes/deletion during the lifecycle; it is not a sandbox against privileged actors. A corpus fingerprint describes projected semantic state, not all omitted package content and not restoration data.

## Rename Coverage

`RenameCoveragePolicy` permits the explicit `coverage.task_properties_not_inspected` omission only when it belongs to a known executable. Those gaps remain visible, and corpus fingerprints retain `IsComplete: false`. This is rename-scoped verification, not certification of full task semantics or execution behavior. Unresolved SQL/expression references, metadata-read failures, unknown omissions, and invalid owner identities remain blocking.

The rename verifier permits the executable name and its companion data-flow name to change for the same native target ID; all other names/metadata, package identity, edge inventory, and coverage counts must match. The staged snapshot is rebuilt after the validator callback so callback-induced projected changes cannot evade this comparison. Native serialization methods are protected virtual hooks for focused save/reload failure injection; default implementations use native SSIS XML serialization/loading.

## Lifecycle Tests

Run `SsisAiRuntime.Ssis16IntegrationTests.exe --mutation-host-probe` after building. Twenty-two scratch-package cases cover successful native save/reload/publication; stale names, missing/duplicate IDs, source hash changes, missing requirements, existing/concurrently created destinations, checkpoint failures/hash mismatch, genuinely asynchronous checkpoint continuation, save/reload failures, validator errors, semantic mismatch, source locking, cancellation, nested and event-handler tasks, missing destination directories, and incomplete coverage. Originals are preserved and staging files removed. The test checkpoint retains a byte-identical artifact copy; validators are metadata-only test doubles. Tests never call native `Validate` or `Execute`.

Run `SsisAiRuntime.Ssis16IntegrationTests.exe --column-widening-probe` for the 10-case widening lifecycle suite. It covers exact native-ID preview, width-only save/reload changes, selected-input identity and mapping retention, stale source/width refusal, topology/type refusal, checkpoint failure, validator mutation, destination preservation, and staging cleanup. Follow both required native gates in the repository build guide before merging changes to these slices.

The inspected fixture lifecycle is verified, not a production mutation deployment. A production checkpoint implementation, an explicit native validation policy, result serialization, authorization/journaling, and SSIS-enabled CI enforcement remain follow-up work. No source-overwrite or agent-facing editing command is provided.