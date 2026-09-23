# Contributing to DragonDeskPet

Thank you for helping improve DragonDeskPet. Keep changes focused, lightweight, and consistent with the desktop-pet-first product direction.

## Before starting

- Search existing issues before opening a duplicate.
- Discuss substantial features or visual redesigns before implementing them.
- Do not include API keys, chat history, local settings, logs, reference images, or other private files.
- Keep V0.2 and later features behind an agreed scope instead of expanding V0.1 incidentally.

## Development environment

DragonDeskPet requires Windows and the .NET 8 SDK. From the repository root:

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\run.ps1
```

The scripts keep .NET CLI and NuGet caches inside the project directory. A valid contribution must build with zero warnings and pass all smoke tests.

## Architecture expectations

- Preserve separation between UI, pet state, AI providers, configuration, and Windows services.
- Route model access through `IAiProvider` and `AiProviderFactory`; do not bind behavior to one vendor.
- Keep the app useful without an API key.
- Keep local data local until an explicit user action sends content to a configured provider.
- Protect stored API keys with Windows user-scoped encryption.
- Require explicit confirmation for high-impact system actions.
- Keep the normal desktop presence compact, quiet, transparent, and lightweight.

See `AGENTS.md` and `docs/REQUIREMENTS.md` for the current product and engineering baseline.

## Visual assets

- Do not commit files from `references/` or unapproved generation candidates.
- Do not submit artwork unless you have the right to contribute it and can explain its source.
- Production state sprites must remain 1241 x 1268 transparent PNG files with consistent character identity and tail anatomy.
- Visual assets require maintainer approval and are governed by `ASSET_LICENSE.md`, not the MIT code license.

## Pull requests

Keep each pull request limited to one coherent change. Include:

- a concise explanation of the problem and solution;
- user-visible behavior changes;
- verification performed;
- screenshots for visual changes;
- privacy, storage, or compatibility implications.

Before requesting review, confirm:

- the solution builds with zero warnings;
- all smoke tests pass;
- no generated output, local data, secret, or unrelated file is included;
- documentation is updated when behavior changes.

Code contributions are submitted under the repository's MIT License. Visual assets remain subject to the separate asset terms and maintainer approval.
