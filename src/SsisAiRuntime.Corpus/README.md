# SsisAiRuntime.Corpus

Runtime-neutral corpus models and baseline operations for SSIS package analysis.

`CorpusSnapshotBuilder` projects the existing `PackageAnalysisSnapshot` and dependency graph into a deterministic, redacted corpus snapshot. It retains dependency edge kinds and allowlisted evidence, aggregates coverage gaps, and canonicalizes transient SSIS system-variable IDs so repeated package loads compare consistently.

`CorpusComparer` reports added, removed, and metadata-changed nodes; edge changes; package identity/name changes; and coverage-gap deltas. `CorpusBaselineStore` reads and writes versioned JSON baselines, validates their structure and compatibility, and atomically replaces approved baselines.

The library does not load, execute, validate, or modify DTSX packages. Package inspection remains the responsibility of `SsisAiRuntime.Ssis16`; the CLI composes an analysis snapshot and passes it to this library.