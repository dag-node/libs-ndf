# NDF

.NET libraries for interoperating with the Linux shell. Call Bash functions from
C# as if they were typed methods — arguments are marshalled in, and `stdout`,
`stderr`, and exit codes are captured and converted back to .NET types.

This repository is a monorepo; each project under `src/` is published as its own
NuGet package under the [DagNode](https://www.nuget.org/organization/DagNode)
organization.

## Packages

| Package | Description |
| --- | --- |
| [`DagNode.NDF.Interoperability.Core`](src/DagNode.NDF.Interoperability.Core) | Run and orchestrate Bash scripts and shell functions from .NET. |

## Install

```bash
dotnet add package DagNode.NDF.Interoperability.Core
```

## Usage

Source a script once and call its functions repeatedly, converting each result to
the requested type:

```csharp
using DagNode.NDF.Interoperability.Model.Bash;

using var bash = await BashScript.CreateAsync(
    bashScriptSettings: new BashScriptSettings("functions.sh"),
    bashProcessSettings: BashProcessSettings.CreateFactoryDefault,
    functionWorkDirSettings: new FunctionWorkDirSettings());

string text  = await bash.CallFunctionAsync<string>("get_string");
int    count = await bash.CallFunctionAsync<int>("get_int");
bool   even  = await bash.CallFunctionAsync<bool>("is_even", ["42"]);
List<string> lines = await bash.CallFunctionAsync<List<string>>("get_array");

// Arguments with spaces are passed through intact.
string joined = await bash.CallFunctionAsync<string>(
    "get_string_from_args_with_spaces", ["Plan 9", "from", "Outer Space"]);
```

`BashScript` sources the script into a single long-lived `bash` process, so
repeated calls avoid re-sourcing. For one-off calls, `FunctionDirect` runs each
invocation as its own `bash -c "source script.sh && ..."`:

```csharp
using DagNode.NDF.Interoperability.Model.Bash;

int value = await FunctionDirect.GetIntAsync("functions.sh", "get_int");
```

`CallFunctionAsync<T>` supports scalars (`string`, `int`, `long`, `double`,
`decimal`, `bool`), enums, and `List<string>` (newline-separated output). Hook
`EventHandlerFunctionStartAsync` / `EventHandlerFunctionFinishedAsync` for raw
command and output access, and `FunctionWorkDirSettings` to control per-call
working directories and log layout.

## Requirements

- .NET Standard 2.1 compatible runtime (`.NET`, or `.NET Framework` via the
  compatibility shim)
- `bash` available on `PATH` (Linux)

## License

[AGPL-3.0-or-later](LICENSE). Copyright © 2026 DagNode.

For packaging or licensing questions, contact `packages@dagnode.com`.
