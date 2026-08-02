---
paths:
  - "**/*.csproj"
  - "**/Directory.Build.props"
  - "**/Directory.Build.targets"
  - "**/Directory.Packages.props"
  - "**/packages.lock.json"
  - global.json
  - "*.sln"
---

# An operator and a confined agent building one .NET tree

One working tree is built by two accounts: an operator, and an agent confined by the
[tools-agent-tools-restricted](https://github.com/dag-node/tools-agent-tools-restricted)
sandbox. The tree is shared on purpose — agent and operator read and write the same files,
review the same diff, and share the same history. What the sandbox separates is its scope.

## What each account holds separately

Each account has its own NuGet package cache, named by `NUGET_PACKAGES` in its environment
and nowhere in this repository, alongside its own `DOTNET_CLI_HOME`. An agent downloads and
writes packages only inside its own cache, so a mistaken or compromised agent cannot alter
the packages another project, operator, or agent restores from. The SDK itself
(`DOTNET_ROOT`) is installed host-wide and read-only to both.

The project tree is group-shared — directories are setgid `operator:ai-tools`, and the agent
is in that group — so both accounts write the same sources, `bin/` and `obj/`. The operator's
*home* directory is not part of that grant: a cache under it stays at owner-only permissions,
readable to nobody else.

That combination is what a shared tree has to account for: **restore records the path of the
cache that ran it, into a directory both accounts share.**

## What the tree records, and what follows

A restore writes `$(NuGetPackageRoot)` into `obj/*.nuget.g.props` and `obj/project.assets.json`
from the restoring account's `NUGET_PACKAGES`. `obj/` is shared, so the tree carries whichever
account restored last, and a build by the other account resolves references and analyzers
from a cache outside the group grant.

| Symptom | Cause | What clears it |
| --- | --- | --- |
| `CS0006` on an analyzer or reference assembly | assets name the other account's cache | `dotnet restore` |
| `NETSDK1064` package not found, though it exists | same | `dotnet restore` |
| `MSB3374` setting a time on an `.Up2Date` marker | the file belongs to the other account | delete the marker |
| `MSB3021` unable to copy over a file in `bin/` | the existing output belongs to the other account and its mode omits group write, as a native apphost's `755` does | delete that output and rebuild |
| `FileNotFoundException` at run time for an assembly present in `bin/` | a package assembly copied from the other account's cache keeps that cache's owner-only mode | delete that `bin/` output and rebuild |
| Errors on the first build after a restore, gone on the next | intermediates settling | build again |

`MSB3374` is an ownership boundary rather than a permission one: the marker is group-writable,
and POSIX still lets only a file's owner set an arbitrary timestamp, so `Touch` fails with
`EPERM` whatever the mode says. Deleting the marker is permitted by the directory's group
write bit, and the next build recreates it owned by the account that ran it.

The same ownership boundary reaches `bin/`. A copy preserves the mode of its source, so package
assemblies copied in from a cache that is owner-only stay owner-only in the shared tree, and
the runtime reports an assembly it cannot read as one it cannot find — naming a missing file
rather than a permission. A native apphost is written `755`, which grants the group execute but
not write, so the next build by the other account cannot copy over it. Both clear the same way:
delete the output and rebuild, which the directory's group write bit permits and which recreates
the file owned by the account that ran the build.

The files are well-formed whenever this happens, and it reproduces with no concurrent build
running: none of it is a torn write.

## Concurrency

MSBuild supports no concurrent build of one `obj/` tree by two accounts, whatever the caches
say. Two accounts building at once corrupt each other's intermediates, and the resulting errors
resemble the ones above without responding to a restore. Builds are serialized between the
accounts by agreement rather than by a lock. This is independent of MSBuild's own worker nodes,
which parallelize a single build safely.

## What stays shared safely

`packages.lock.json` records a package id, version, content hash and dependency list, and no
path. Both accounts produce byte-identical lock files, a restore by either leaves nothing to
commit, and locked-mode restore behaves the same for both. Lock files add no step here.

`bin/` and `obj/` are ignored by git, so none of this reaches a commit.

## SELinux policy groups

Optional groups govern what the sandbox may do while building. Each is off by default and
enabled with `sudo selinux/install-selinux.sh enable-group <group>` in the sandbox repository.
They cover disjoint operations and none implies another, so a full build-and-run workflow
enables all three.

- **`tmpmap`** covers restore and build.
- **`apphost`** lets the sandbox build .NET executable and host projects — console apps,
  ASP.NET Core and worker services, xunit.v3 tests, single-file publishes. A class library, or
  in-process MSTest on Microsoft.Testing.Platform, does not need it.
- **`netcore`** (experimental) covers .NET runtime IPC — `dotnet test` and multi-node MSBuild
  pipes — and running a project's built executable.

What each grant is worth, measured on this repository:

| Capability | Without the groups | With them |
| --- | --- | --- |
| Solution restore and build | hangs with no output unless given `-m:1` | multi-node, a solution rebuild in seconds |
| Building an executable project | fails in `CreateAppHost` | succeeds at the default `UseAppHost=true` |
| `dotnet publish` | fails at the same step | succeeds |
| Running the built native host from the tree | refused at `execve` | runs |
| `dotnet test` | no connection to its test host | reports every suite |

With all three enabled the agent and the operator run the same commands: `dotnet restore`,
`dotnet build`, `dotnet test`, `dotnet publish`, and the project's own executable, none of them
needing a flag the other account does not use. Where a group is absent the corresponding row
above names both the failure and the substitute — `-m:1` for a build,
`-p:UseAppHost=false` for a project that need not run, and `dotnet exec <assembly>.dll` for a
suite.

## Design notes

A restore costs about a second on this repository, so restoring before a build is cheaper than
diagnosing what the previous account left behind. The convention is therefore unconditional
rather than conditional on noticing a symptom.
