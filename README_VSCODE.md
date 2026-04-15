# MineraScope in VS Code

This solution can be built and run from VS Code on Windows.

## Recommended extensions

- `ms-dotnettools.csdevkit`
- `ms-dotnettools.csharp`
- `ms-dotnettools.vscode-dotnet-runtime`

## Open

Open `MineraScope.code-workspace` in VS Code.

The workspace includes:

- `MineraScope`
- `Crystallography` from `Crystallograohy`

That matches the project paths used by `MineraScope.sln`.

## Build

Use one of these:

- `Terminal` -> `Run Task` -> `dotnet: build solution`
- `dotnet build MineraScope.sln`

## Run / Debug

Press `F5` and select `.NET Launch MineraScope`.

Or run:

- `Terminal` -> `Run Task` -> `dotnet: run MineraScope`
- `dotnet run --project MineraScope\MineraScope.csproj`

## Notes

- This is a Windows Forms app, so it should be run on Windows.
- The current target framework is `net10.0-windows10.0.26100.0`.
- Confirmed with `dotnet build MineraScope.sln` on this machine.

