# Mutation Host Prototype

This .NET Framework 4.8 x64 library composes native SSIS loading, mutation previews, corpus fingerprints, checkpoint and validation callbacks, and staging for task renaming. The supplied prototype has been repaired using native lifecycle regression tests. It is not wired into the inspection CLI or developer FlowRunner, and provides no executable entry point or production checkpoint/validation service.

## Composition Boundaries

- The host references Core, Inspectors, AI, Corpus, Mutations, and Ssis16. No portable project depends on the host. Deployment requires Windows x64, .NET Framework 4.8, and the installed SSIS 16 runtime; the library does not package the native runtime.
- `IMutationCheckpointService` is injected. Storage, compression, retention, restore layout, and protection of checkpoint contents belong to its implementation. A checkpoint must preserve the full DTSX artifact; returning a fingerprint or hash alone does not establish that restoration is possible.
- `INativeMutationValidator` is injected. Its implementation must explicitly define available checks and resource access and return safe diagnostic codes. Successful loading is not native validation, and a stub reporting success is not verification that the required validation occurred.
- `MutationHostResult` is a distinct lifecycle-evidence contract, not the two-field `MutationResult`. The existing `MutationContractSerializer` does not serialize it; a dedicated, versioned host-result serializer remains follow-up work.
- The host rebuilds the preview and resolves the target against native task identity, saves with native `SaveToXML`, and reloads the staged XML before its validation and rename-diff checks. It depends on the injected implementations to provide truthful checkpoint and validation evidence.

The host holds the source read-only from hashing/loading through checkpoint creation and publication. The stager requires a different destination, refuses an existing destination, and opens another source read lock while staging. Both original and reloaded native packages are disposed, including load failures. Source file sharing prevents normal Windows writes/deletion during the lifecycle; it is not a sandbox against privileged actors. A corpus fingerprint describes projected semantic state, not all omitted package content and not restoration data.

## Rename Coverage

`RenameCoveragePolicy` permits the explicit `coverage.task_properties_not_inspected` omission only when it belongs to a known executable. Those gaps remain visible, and corpus fingerprints retain `IsComplete: false`. This is rename-scoped verification, not certification of full task semantics or execution behavior. Unresolved SQL/expression references, metadata-read failures, unknown omissions, and invalid owner identities remain blocking.

The rename verifier permits the executable name and its companion data-flow name to change for the same native target ID; all other names/metadata, package identity, edge inventory, and coverage counts must match. The staged snapshot is rebuilt after the validator callback so callback-induced projected changes cannot evade this comparison. Native serialization methods are protected virtual hooks for focused save/reload failure injection; default implementations use native SSIS XML serialization/loading.

## Lifecycle Tests

Run `SsisAiRuntime.Ssis16IntegrationTests.exe --mutation-host-probe` after building. Twenty-two scratch-package cases cover successful native save/reload/publication; stale names, missing/duplicate IDs, source hash changes, missing requirements, existing/concurrently created destinations, checkpoint failures/hash mismatch, genuinely asynchronous checkpoint continuation, save/reload failures, validator errors, semantic mismatch, source locking, cancellation, nested and event-handler tasks, missing destination directories, and incomplete coverage. Originals are preserved and staging files removed. The test checkpoint retains a byte-identical artifact copy; validators are metadata-only test doubles. Tests never call native `Validate` or `Execute`. Follow the required regression gate in the repository build guide before merging changes to this slice.

The inspected fixture lifecycle is verified, not a production mutation deployment. A production checkpoint implementation, an explicit native validation policy, result serialization, authorization/journaling, and SSIS-enabled CI enforcement remain follow-up work. No source-overwrite or agent-facing editing command is provided.