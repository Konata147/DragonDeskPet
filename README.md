# DragonDeskPet

A Windows-native Q-version dragon-girl desktop pet and provider-neutral AI assistant.

## Current milestone

V0.1 is feature-complete. It includes the transparent draggable pet, seven illustrated states, tray controls, portable settings, a compact chat bubble, single-instance restore behavior, first-run guidance, crash logs, and a provider-neutral AI boundary.

## Run

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.dotnet-cli"
$env:NUGET_PACKAGES = "$PWD\.nuget\packages"
dotnet run --project .\src\DragonDeskPet\DragonDeskPet.csproj
```

Convenience scripts are available in `scripts/`: `build.ps1`, `test.ps1`, `run.ps1`, and `publish.ps1`. Each script pins writable caches and outputs to this project drive.

The validated Release output is written to `dist/DragonDeskPet/`.

Double-click the pet to open chat, right-click for the menu, drag to move, and use the mouse wheel to resize. Releasing a drag outside the character still completes the drop and returns through Happy to Idle. Closing the visible window hides it; use the tray menu or launch the EXE again to restore it.

## Storage

The project, build output, .NET CLI cache, NuGet package cache, runtime settings, and crash logs all stay on the same drive as this repository/application. Runtime data is written to a `data/` folder beside the executable; API keys remain protected with Windows user-scoped encryption. Crash logs contain diagnostics only, redact configured secrets, and retain the latest ten files.

## Art

The seven production sprites are stored in `assets/character/`: `default.png`, `hover.png`, `dragged.png`, `thinking.png`, `happy.png`, `angry.png`, and `sleeping.png`. Every sprite uses the same transparent 1241 × 1268 canvas and the approved scaled-tail anatomy. A missing or damaged state image falls back to `default.png`; reference images under `references/` are never loaded at runtime.

## License

The software source code is available under the [MIT License](LICENSE). Character artwork, the application icon, and other visual assets are excluded from the MIT License and are governed by [ASSET_LICENSE.md](ASSET_LICENSE.md).
