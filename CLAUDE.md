# libs-ndf — agent instructions

.NET ↔ Bash interoperability libraries. A consumer calls shell functions as typed C#
methods; the libraries marshal arguments in and convert `stdout`, `stderr`, and exit codes
back to .NET types. `README.md` documents the consumer-facing API — this file documents
what an agent working *on* the code needs.

## How instructions are organized

- **This file** — the repository layout, the invariants that hold across every component,
  and cross-cutting conventions an agent needs in every session.
- **`.claude/rules/*.rule.md`** — per-component reference prose, scoped to the source files
  it describes via `paths:` frontmatter, so it loads when you open a matching file under
  `src/` (or `tests/`). See the component map below. A rule and its source file's header
  overlap by design and are bidirectionally coupled: changing either obligates reconciling
  the other, resolving any conflict against the code, never defaulting to one side. Adding,
  moving, or renaming a source file a rule documents obligates updating that rule's `paths:`
  in the same change — the file→rule auto-load is only as complete as `paths:`, and a
  documented file left out of it silently stops loading its rule.
  Conventions for writing rules: `.claude/rules/authoring.rule.md`.
- **Auto memory** (`/memory`) — decisions, rejected alternatives, and open follow-ups
  that are not derivable from the code.

Code is the source of truth for all three. Each fact is single-sourced at the layer that
owns it and linked from the others rather than restated, so a behaviour change has one
place to land. A growing docs-to-code ratio is a code smell: where a header or rule has to
narrate the code line by line, the code is the refactor candidate, not the prose — flag it
rather than documenting around it.

## Component map

| Rule | Domain |
| --- | --- |
| [authoring](.claude/rules/authoring.rule.md) | Conventions for the rule files in this directory. |
| [tests](.claude/rules/tests.rule.md) | Test project layout, categories, and the hermeticity contract. |

Rules covering `src/` components are added as those domains appear; a file fully explained
by its own header stays uncovered.

## Repository layout

- `Directory.Build.props` — repository-wide build settings, imported explicitly by the
  `src/` and `tests/` props files. MSBuild stops at the nearest `Directory.Build.props`
  walking up from a project, so this file reaches a project only through that import.
- `src/<PackageId>/` — one project per NuGet package. `src/Directory.Build.props` carries
  the shared package metadata, licensing, analyzer, and documentation settings; each
  `.csproj` sets only `PackageId`, `Version`, `Description`, and `PackageTags`.
- `src/DagNode.NDF.Interoperability.Core/` — the library. Targets `netstandard2.1`, so its
  public surface is consumable from both `net8.0` and `net10.0`. Its only package
  dependency is `Microsoft.Extensions.Logging.Abstractions`.
- `src/DagNode.NDF.Interoperability.Bash.ConsoleApp/` — a manual harness targeting
  `net8.0;net10.0`, with sample scripts under `Scripts/`. Not published.
- `tests/` — test projects, outside the `src/Directory.Build.props` scope so nothing under
  it inherits packaging settings. See [tests](.claude/rules/tests.rule.md).

## Invariants

These hold across components. A change that breaks one is a change to the protocol, not a
refactor.

### The bash process speaks one line at a time

`BashProcess` drives a single long-lived `bash` whose stdin receives one command per
`WriteLineAsync`, and whose stdout is read line by line. Every string written to that stdin
therefore occupies exactly one physical line, and every line the library emits for its own
consumption is a marker line it can recognize by position.

### Function bodies travel base64-encoded

`GlobalScripts.ValidateAndSourceInlineFunctionWithSourcingResult` encodes a function body
with `Convert.ToBase64String` and decodes it in bash. The base64 alphabet contains no shell
metacharacter, so the payload needs no escaping and reaches bash byte-exact — arbitrary
bodies, including here-docs, `case` statements, and backslash escapes, survive transport.

Flattening a script to one line and escaping it for an `echo -e` reader is lossy in both
passes and is confined to the deprecated members in the `Deprecated inlining` regions of
`GlobalScripts` and `LinuxUtils`. Those members throw rather than emit a line that behaves
differently from the script it came from, and they carry `[Obsolete]` with the replacement
named. New sourcing paths use the base64 transport.

### Marker lines are position-parsed

`FunctionParser` reads `___END_FN__` by fixed field offsets (marker, start ns, end ns,
duration, marker tag, exit code) and `___END_SOURCE_FN__ <object> <result>` by taking the
last whitespace-separated token as the result, so a sourced path containing spaces still
parses. Changing what any component emits obligates changing the offsets in the same change.

### Untrusted input fails closed

Values interpolated into generated bash are admitted by allowlist, not filtered by
blocklist: `ValidateAndSourceInlineFunctionWithSourcingResult` accepts a function name only
when it matches `^[A-Za-z_][A-Za-z0-9_]*$`. Where a transformation cannot preserve meaning,
the code throws and names the construct rather than emitting something that parses but
behaves differently.

### Logging goes through abstractions, and script output is data

The library depends on `Microsoft.Extensions.Logging.Abstractions`; the consuming
application chooses and wires providers. Text captured from a subprocess is passed as a log
*argument*, never as a message template — `_logger.LogDebug("{ScriptOutputLine}", line.ToLogLine())` —
so braces in script output cannot be read as template holes. `ToLogLine`/
`EscapeControlCharacters` escape control characters, so a captured line cannot move the
cursor, emit ANSI sequences, or span lines in a log.

### Async paths do not block

Every `await` carries `ConfigureAwait(false)`. Subprocess stdout is drained before awaiting
exit, so neither the caller's thread nor the child stalls on a full pipe buffer.

### Subprocess cleanup is trapped

`___global__on_stop` clears its traps and signals the whole process group, so terminating
the main bash process does not leak background subprocesses.

## Conventions

- **Language level** — `LangVersion` is `latest` with `Nullable` and `ImplicitUsings`
  enabled. `netstandard2.1` bounds the *runtime* API surface available in
  `...Interoperability.Core`, not the language features.
- **Naming — code carries its own explanation.** Identifiers are long and descriptive
  enough that a statement reads as prose (`unterminatedQuote`, `ThrowIfNotInlinable`,
  `SOURCING_END_MARKER`), so behaviour is legible without a comment restating it. A comment
  that paraphrases the line below it is a signal to rename; comments earn their place by
  carrying what the code cannot — a protocol constraint, a rejected alternative, the reason
  a step exists.
- **Style** — tabs for indentation, opening brace on the same line, `_camelCase` for private
  fields, `s_camelCase` for private static fields, `SCREAMING_CASE` for constants that name
  a wire-protocol token. Match the surrounding file.
- **Doc comments** — public members carry XML docs (`GenerateDocumentationFile` is on;
  CS1591 is suppressed while the pass completes). Use the `doc-comments` skill for member
  summaries and the `reference-docs` skill for file headers and this file.
- **Analyzers** — `EnableNETAnalyzers` is set explicitly, since the SDK enables it by
  default only for `net5.0`+ targets. A build that reports zero warnings is the baseline.
- **Commits** — [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/), one
  concern per commit, with a version bump as its own `chore(release):` commit. Package
  versions come from each `.csproj` `<Version>`, which the release workflow reads. Use the
  `commit-messages` skill.
- **Dependencies** — each project carries a `packages.lock.json`, so a dependency change and
  its lock file land in one diff and a restore in locked mode reproduces the graph. A
  `PackageReference` version is a *floor*: NuGet resolves the lowest compatible version, so
  the floor stays where it is and rises for a security advisory or an API the code needs,
  not to track a release. Restore audits the whole graph and reports a known vulnerability
  at build time, which is what surfaces a fix worth taking.
- **SDK** — `global.json` pins the lowest SDK the build is known to work with and rolls
  forward to any newer feature band, so a machine on a distribution-paced SDK and a runner on
  the newest one both build. The pin moves when the lowest supported toolchain moves.

## Building

```bash
dotnet restore -m:1
dotnet build --no-restore -c Release -m:1
dotnet exec tests/<project>/bin/Debug/net10.0/<project>.dll   # see the tests rule
```

Development runs under the
[tools-agent-tools-restricted](https://github.com/dag-node/tools-agent-tools-restricted)
sandbox, which confines the agent account with SELinux. Two loadable dotnet module groups,
`tmpmap` and `apphost`, govern what a build can do, each administered with
`ai-tools-admin selinux enable-group <group>`:

- **`tmpmap`**
  ([policy](https://github.com/dag-node/tools-agent-tools-restricted/blob/develop/selinux/policy/ai_tools_tmpmap.te))
  carries the on-disk temporary-file mapping MSBuild needs. Building this repository depends
  on it; without it a solution-level `dotnet restore` or `dotnet build` produces no output
  and hangs until it is killed. Solution-level MSBuild runs single-node, `-m:1`.
- **`apphost`** carries the in-memory execution a project needs where its output is a native
  host — a console app, a single-file publish, an out-of-process test host. The test projects
  set `UseAppHost=false` and run through `dotnet exec`, so the suite builds and runs without
  it, and `-p:UseAppHost=false` covers the console app the same way.

The two groups cover disjoint operations and neither implies the other. Enabling either is
the operator's call. Report the blocker and the command that reproduces it rather than
changing host policy.

An `obj/` tree written by another account is not rewritable, so an incremental build over an
operator's artifacts fails on the up-to-date marker (MSB3374), and assets naming a package
cache the agent cannot read fail the compile (CS0006). `dotnet restore -m:1` rewrites those
assets against the agent's own cache, which is resolved from the environment and named
nowhere in this repository.
