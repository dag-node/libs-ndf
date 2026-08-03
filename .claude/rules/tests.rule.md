---
paths:
  - tests/**
---

# Test organization and invariants

Tests live under `tests/`, outside the scope of `src/Directory.Build.props`, so no test
project inherits package metadata or is publishable. `tests/Directory.Build.props` carries
the settings shared by every test project — `net10.0`, `IsPackable=false`,
`IsTestProject=true`, nullable and implicit usings, analyzers — and
`tests/Directory.Packages.props` centralises test package versions, so a framework upgrade
is one edit.

The stack is MSTest on
[Microsoft.Testing.Platform](https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-intro):
`EnableMSTestRunner` builds each project as its own test application that runs the tests in
its own process. The Rider test explorer on RHEL 9+, the Visual Studio 2026 test explorer on
Windows, and `dotnet test` all drive it, and `dotnet exec <project>.dll` runs a built suite
with nothing but the shared host. Every project targets `net10.0` alone and depends only on
packages available on both platforms.

## Naming and layout

One project per library × category:

```
tests/
  Directory.Build.props
  Directory.Packages.props
  TestSupport/                                     # helpers compiled into each project
  DagNode.NDF.Interoperability.Core.UnitTests/
  DagNode.NDF.Interoperability.Core.IntegrationTests/
  DagNode.NDF.Interoperability.Core.BoundaryTests/
```

A library added under `src/` gets its own `<PackageId>.<Category>Tests` projects, so the
project name identifies both the subject and the category and each category runs on demand
by project path. Within a project, the file layout mirrors the subject's: a test for
`Bash/GlobalScripts.cs` lives in `Bash/GlobalScriptsTests.cs`.

Separate projects are what make a category runnable alone without a filter expression, and
what the IDE explorers group by. `[TestCategory]` narrows further within a project.

`tests/TestSupport/*.cs` holds the helpers shared across categories — the bash host, the
temporary directory, the corpus — and each project compiles them in through a `<Compile
Include="../TestSupport/*.cs" />` item rather than referencing a project of their own, so a
helper needs no packaging decision.

## Categories

- **Unit** — one type or method, no process, no filesystem, no `bash`. Runs on every
  platform, including Windows, so the string, parser, and code-generation logic is covered
  wherever a contributor works. Generated bash is asserted as *text*: shape, single-line
  transport, escaping, allowlist refusals.
- **Integration** — the library driving a real `bash` process end to end: source a script,
  call a function, parse the markers, observe exit codes and timings. Requires `bash` and
  Linux.
- **Boundary** — the guarantee that a hostile or malformed input cannot reach a dangerous
  state: payloads that attempt to break out of the generated quoting, names that attempt
  injection, bodies containing every construct the transport claims to carry. Requires
  `bash` and Linux.

A test that requires `bash` calls `BashRequirement.SkipUnlessAvailable()` from its
`[TestInitialize]`, so it reports inconclusive rather than failing where bash is absent: the
Windows and Linux runs are both green and the reason names what is missing.

## Hermeticity contract

A test run leaves the host as it found it. Concretely, a test:

- creates every file it needs under a unique temporary directory it owns, and removes that
  directory when it completes;
- runs as an unprivileged user — nothing requires root, and nothing mounts a filesystem.
  Code paths that mount `tmpfs` are covered by asserting the *generated command*, with the
  mount itself mocked;
- signals only processes it started. The library's cleanup path signals an entire process
  group, so a test that exercises it starts its subject in a process group of its own;
- depends on no network, no ambient environment variable, and no state left by another
  test, so any subset runs in any order.

Prefer a mock or a generated-text assertion over an effect on the host wherever both cover
the same guarantee.

## Two-ended assertions

A protocol or safety guarantee is covered by a **pair** of tests, not one, and the pair is
what makes the coverage meaningful:

- a **runtime** assertion that the refusal fires — drive the code into the bad state (a
  function name that is not an identifier, a body that flattening cannot preserve, a
  malformed marker line) and assert it refuses, names the construct, and produces no output
  that a downstream reader would accept;
- a **boundary** assertion that the bad state is unreachable — take the same generated
  command, execute it in a real `bash`, and assert the payload cannot escape its quoting,
  that the body arrives byte-exact, and that the command occupies exactly one line.

Neither is sufficient alone. The runtime half catches a caller passing input the code must
reject; the boundary half catches a transport that silently rewrites a payload it claims to
carry. A guarantee with only the first is untested against its actual adversary; one with
only the second silently stops refusing the day the predicate regresses.

The sourcing transport is the worked example. The runtime half drives hostile function
names and unflattenable bodies through `GlobalScripts` and `LinuxUtils` and asserts the
allowlist and the fail-closed throws; the boundary half feeds the generated line to a real
`bash`, compares `declare -f` against sourcing the same body from a file, and asserts the
two are byte-identical for bodies containing here-docs, `case … ;;`, `${v#p}`, `$#`,
`printf '\t'`, `'\0057'`, and `'\\d'`. A new guarantee lands with both halves, in the
category each belongs to.

## Design notes

Marker parsing is covered by table-driven cases over whole lines rather than by
reconstructing offsets in the test, so a field-width change fails the test that asserts the
protocol instead of silently agreeing with itself.

## Quirks

- **Signal traps take the test host with them.** `RUN_SETUP___global__on_stop` installs
  `EXIT`/`SIGTERM`/`SIGINT`/`SIGHUP` traps that signal the whole process group. A test that
  writes those traps into a `bash` sharing the runner's process group terminates the runner
  and truncates the run. Give that subject its own process group, or assert the generated
  text instead of executing it.
- **The runner needs no native apphost and no test-host process.** In-process
  Microsoft.Testing.Platform runs a suite from the test assembly itself, so
  `dotnet exec <project>/bin/<configuration>/net10.0/<project>.dll` reports a full run with
  no sandbox policy group enabled — the substitute to reach for where `dotnet test` cannot
  report. xunit.v3 requires a native apphost and a VSTest-hosted runner requires its own test
  host, either of which makes a suite depend on a policy group being enabled. See
  [shared-tree](shared-tree.rule.md).
- **Bodies round-trip only through the base64 transport.** The deprecated inlining members
  throw on constructs they cannot preserve, so a test that expects a here-doc or a `case`
  statement to survive is asserting against the base64 path, not the inlining path.

## Deferred

- Coverage of `FunctionDirect` and the work-directory layout. `BashScriptPipelineTests` covers
  `BashScript`/`FunctionProcessor` end to end (see [process-lifecycle](process-lifecycle.rule.md));
  `FunctionDirect` and the on-disk `{prefix}` layout are not yet exercised directly.
- Property-based coverage of the transport — arbitrary bodies asserted byte-exact through a
  real `bash` — extending the worked example from a fixed corpus to generated input.
- A coverage gate. Collection runs per category before a threshold is enforced, so the
  number reflects a full run rather than whichever category ran last.
