# SSIS Developer Flow Runner

Separate Windows x64 .NET Framework 4.8 executable for trusted local developer execution using SSIS 16. The inspection CLI and adapter remain unchanged. No portable project depends on this runner.

```console
SsisAiRuntime.FlowRunner.exe components
SsisAiRuntime.FlowRunner.exe demo
type request.json | SsisAiRuntime.FlowRunner.exe run
```

## Installed Components

`components` lists every pipeline component registration returned by the local SSIS runtime, including built-in and any installed optional/custom components. It does not instantiate the components, create a package, or execute a flow. The catalog reports exact totals and deterministic metadata ordering without a result cap. This is installation-specific discovery, not a guarantee that every delivered component or provider is present.

Each entry includes `name`, `creationName`, `componentType`, and separate capability flags:

- `discovered`: registered on the current installation.
- `configurable`: the current runner has a construction recipe for the selected registration.
- `executionTestAvailable`: that recipe has execution checks in the opt-in native harness.
- `executionTestedThisInvocation`: always false for catalog discovery; listing is not testing.
- `recipe`: the implemented synthetic recipe identifier for a supported registration, otherwise null.

Currently Flat File Source, Derived Column, Data Conversion, and Flat File Destination have recipes. Shared definitions from `catalog/components` are embedded in the runner and matched by SSIS major version and exact creation name. Installed registrations include `sharedDefinition`; known definitions not installed locally appear in `uninstalledDefinitions`. Historical catalog evidence and recipe availability are not claims of execution during discovery. Other discovered components remain visible for future support; `configurable: false` does not mean the native component is broken or unsupported by SSIS. No component is automatically executed based on discovery alone.

## Synthetic Execution

The recipe constructs Flat File Source -> Derived Column -> Flat File Destination using installed component registrations discovered from native SSIS metadata. `demo` uses integers `-2`, `0`, and `3` with `Value + 1`. `run` reads a versioned JSON request from stdin and executes its expression over the supplied synthetic integers. Both write calculated and original values into a disposable CSV and require native execution to succeed, the expected row count, and exact original/calculated values in input order. No database or caller-supplied package is used.

```json
{
	"schemaVersion": "1.0",
	"values": [-2, 0, 3],
	"expression": "Value * 2",
	"expectedValues": [-4, 0, 6]
}
```

Numeric input and expected output contain the same number of Int32 values, between 1 and 1,000. Expressions are native SSIS Derived Column expressions referencing `Value`; the calculated output is also Int32. Requests are limited to 65,536 characters and expressions to 4,096 characters. Legacy version `1.0` requests remain valid. Version `1.1` adds recipe selection. Duplicate properties, unknown fields, invalid rows, and inconsistent counts are rejected before launching the worker. Close stdin after sending the request. The validated request is passed to the worker through stdin, not interpolated into process arguments. Native syntax errors, conversion/overflow errors, and wrong expected values produce failure rather than provisional success.

### Data Conversion

```json
{
	"schemaVersion": "1.1",
	"recipe": "data-conversion",
	"values": [-32768, 0, 32767],
	"conversionType": "Int16",
	"expectedValues": [-32768, 0, 32767]
}
```

Supported targets are `Int16` and `Int64`. This tests native narrowing/widening from Int32, not a replacement conversion implementation. Expected values remain Int32-representable because the inputs are Int32. Native regressions cover Int16 boundaries, overflow refusal, and Int64 widening.

### Flat File Text

```json
{
	"schemaVersion": "1.1",
	"recipe": "flat-file-text",
	"values": ["Long student name"],
	"expectedValues": ["Long student name"],
	"sourceWidth": 4,
	"destinationWidth": 4,
	"widenTo": 64
}
```

The text recipe uses Unicode local files and a single `Value` column. Widths range from 1 to 4,000. Each string is at most 4,000 characters and must not contain commas, quotes, or control characters; arbitrary quoted/multiline CSV and nulls are not supported yet. Without `widenTo`, a narrow source can fail natively on truncation. Narrow destination metadata is rejected by the harness because delimited Flat File output does not itself reliably truncate to its declared width.

With `widenTo`, the already-constructed flow is edited through `NativeFlatFileColumnEditor.Widen`: connection-manager widths, source output/external metadata, destination external metadata, and selected input mapping are updated together. The target width cannot shrink either declared width. Execution must preserve exact expected strings, including Unicode characters.

## Coordinated Column Edits

`NativeFlatFileColumnEditor` provides callable in-memory `Widen`, `Shrink`, `Add`, and `Remove` operations. Addition and removal currently support direct Unicode delimited Flat File flows and update source/destination external-column mappings and delimiters. Shrinking and removal require explicit acknowledgement of possible data loss; acknowledgement is not proof that existing data fits a reduced width.

The native metadata fixture starts from a serialized/reloaded contrived package, performs all four operations, then verifies native XML and reload results. OLE DB destination edits modify SSIS external-column metadata only; they do not create, alter, or validate a physical database table. There is no edit command for caller-supplied DTSX files, no completed save/checkpoint workflow, and no claim of automatic handling of arbitrary branches or transformations.

The parent creates a unique temporary directory and starts a child worker. It imposes a 60-second execution timeout, terminates a timed-out worker, and removes the owned input/output directory after the worker exits. Cleanup failure is reported as failure. Abrupt parent termination can leave temporary artifacts. There is no fixture-retention option. The internal worker entry point requires an empty probe directory directly under the temporary root.

Every invocation returns one JSON envelope with schema version, command, status code, and exit code. Execution commands (`flow.demo` or `flow.run`) also return row count and execution mode; `flow.components` returns catalog counts and registration metadata instead. Raw paths, expressions, row values, and native exception/error messages are not returned. Exit `0` means discovery succeeded or the recipe executed and its output assertions passed; `2` means invalid arguments or request structure; `4` means discovery, construction, execution, assertion, timeout, worker, or cleanup failure.

This is trusted developer execution, not a security sandbox: the worker runs with the caller's permissions. The implemented recipe uses only local fixture files and known built-in components; callers should review the expression and expected values before invoking `run`. General multi-component flow specifications, scripts/custom-component configuration, external-effect review workflows, and database-connected probes remain future work. Invoking `demo` or `run` requests execution of this synthetic recipe, not authorization to execute real jobs.

Developer-facing native messages, actionable mismatch details, and preserving the original error when cleanup also fails are planned improvements. Current failures still emit stage-level codes rather than detailed native diagnostics. The production inspection CLI's redaction policy is separate from this harness's intended debugging experience.