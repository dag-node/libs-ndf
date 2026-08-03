# Changelog

All notable changes to `DagNode.NDF.Interoperability.Core` are recorded here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). While the major version is `0`, a
breaking change raises the minor version.

## [0.10.1] - 2026-08-03

### Fixed

- `BashScript` pipeline calls stay responsive across create/dispose cycles. The bash stdout and
  stderr readers now run for the whole life of their process and stop only on disposal, so every
  result marker is read and each call returns its result. Thread, file-descriptor, and child-process
  counts stay flat across cycles.
- A call now returns promptly with an `InteroperabilityException` naming the exit code when the
  resident bash exits on its own — a crash, an OOM kill, or a hit resource limit — so the caller gets
  a clear, immediate error and the session stays responsive.

## [0.10.0] - 2026-08-03

### Changed

- **Breaking.** Public async methods take a `CancellationToken` rather than a
  `CancellationTokenSource`; `BashScript.CreateAsync` and `FunctionDirect.GetBoolAsync` no longer
  accept a source. A caller bounds startup and per-call waits without being able to tear down the
  resident session, which the instance owns.
- **Breaking.** The non-functional timeout overload is replaced by a working per-call timeout
  alongside the cancellation token.
- Concurrent calls on one instance no longer serialize on the reader thread: result waits complete
  asynchronously.

### Added

- A real, configurable per-call timeout. A timeout or cancellation also terminates the bash
  function's own process tree, not just the wait — each instance's bash runs in its own `setsid`
  session and every call reports its PID, so `ProcessTree` reaps the subtree (`pidfd` where
  available, else `/proc` + `kill(2)` with a start-time reuse guard) using no `pgrep`/`ps`/`kill`
  binary, so a locked-down sandbox can still clean up.
- `IAsyncDisposable` shutdown that drains in-flight calls before tearing the session down.
- A caller-supplied result parser and split separator, and case-insensitive enum parsing.

## [0.9.0] - 2026-08-02

### Changed

- **Breaking.** Function definitions travel into `bash` base64-encoded instead of flattened to
  one line and escaped for `echo -e`. A body reaches `bash` byte-exact, so here-docs, `case`
  statements, `${v#p}`, `$#`, and backslash escapes such as `\t` and `\0057` now define the
  function as written. Calls through `BashScript` and `FunctionDirect` are unaffected.
- **Breaking.** `LinuxUtils.InlineAndEscapeBashScript` throws an `ArgumentException` naming the
  construct it cannot preserve — a here-doc, a `case` statement, a string spanning lines, a
  residual backslash, or a line ending in an operator — where it previously emitted a line that
  behaved differently from its script.
- **Breaking.** `Extensions.AddInlineSemicolons` terminates a line ending in `}`. A statement
  ending in an expansion such as `local p=${1#/tmp/}` keeps its separator instead of running
  into the line below it.
- Sourcing reports `SOURCED_SUCCESSFULLY` only once the named function is defined, and rejects
  a body that fails a syntax check before any of it runs.
- A sourcing result line parses when the sourced path contains spaces.

### Added

- A test suite under `tests/`, on MSTest and Microsoft.Testing.Platform: unit, integration, and
  boundary projects run on demand from `dotnet test` or either IDE test explorer.
- `packages.lock.json` for every project, so `dotnet restore --locked-mode` reproduces the
  dependency graph, and restore audits the whole graph for known vulnerabilities.

### Deprecated

- `LinuxUtils.InlineAndEscapeBashScript`, `GlobalScripts.InlineAll`,
  `GlobalScripts.ConvertBashScriptToInline`, `Extensions.AddInlineSemicolons`,
  `Extensions.RemoveBashComments`, and `Extensions.ConcatenateBashMultilineContinuations` carry
  `[Obsolete]`. They remain for rendering a readable one-liner; sourcing no longer uses them.

### Security

- A function name interpolated into generated `bash` is admitted by allowlist
  (`^[A-Za-z_][A-Za-z0-9_]*$`), and a base64 payload carries no shell metacharacter, so a
  function body cannot escape its quoting.

### Requirements

- `base64`, `date`, `mkdir`, and `sleep` (coreutils) and `ps` and `pgrep` (procps) must be on
  `PATH`. `base64` is needed for any call at all. A standard distribution carries them; a
  minimal container image may not.

## [0.8.2] - 2026-07-29

### Changed

- **Breaking.** The package depends on `Microsoft.Extensions.Logging.Abstractions` alone. An
  application chooses and wires its own logging providers.

### Fixed

- Script output is logged as data rather than as a message template, so braces in captured
  output are no longer read as template holes.
- Asynchronous paths no longer block, and `ConfigureAwait(false)` covers every await.

### Added

- XML documentation for the types a consumer starts from.

## [0.8.1] - 2026-07-29

### Added

- A package icon.

## [0.8.0] - 2026-07-29

First published release.

### Added

- `BashScript` for sourcing a script into one long-lived `bash` process and calling its
  functions repeatedly, and `FunctionDirect` for one-off calls.
- `CallFunctionAsync<T>` conversion to `string`, `int`, `long`, `double`, `decimal`, `bool`,
  enums, and `List<string>`.
- `FunctionWorkDirSettings` for per-call working directories and log layout, and the
  `EventHandlerFunctionStartAsync` / `EventHandlerFunctionFinishedAsync` hooks for raw command
  and output access.

[0.10.1]: https://github.com/dag-node/libs-ndf/compare/v0.10.0...v0.10.1
[0.10.0]: https://github.com/dag-node/libs-ndf/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/dag-node/libs-ndf/compare/v0.8.2...v0.9.0
[0.8.2]: https://github.com/dag-node/libs-ndf/compare/v0.8.1...v0.8.2
[0.8.1]: https://github.com/dag-node/libs-ndf/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/dag-node/libs-ndf/releases/tag/v0.8.0
