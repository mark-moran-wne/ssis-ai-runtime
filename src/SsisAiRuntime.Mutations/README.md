# SsisAiRuntime.Mutations

This project contains runtime-neutral mutation contracts and a read-only preview for `RenameTask` only. Targets are selected by `SemanticObjectKind` and exact native ID; names are never used to resolve execution targets. Preview impact comes from the dependency graph's rich impact query.

Incomplete dependency coverage prevents a valid execution plan from being produced. The project defines execution request/result and `IMutationExecutor` contracts but deliberately provides no executor. Native mutation, checkpointing, save-as, reload, validation, semantic diff, and journaling remain deferred until their lifecycle is explicitly designed and tested.

`MutationContractSerializer` emits deterministic JSON envelopes with `schemaVersion` `1.0` for requests, previews, execution plans, and results. `MutationExecutionStatus` distinguishes preview-only, checkpoint, validation, save, reload, and completion states; there is no ambiguous success boolean.