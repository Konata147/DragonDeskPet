# GPT Dragon Desktop Pet — V0.1 requirements

## 1. Product goal

Build a small Windows desktop companion that feels like a living chibi dragon girl first and an AI assistant second. V0.1 validates the desktop-pet experience and the long-term architecture; it intentionally avoids the large feature backlog.

## 2. Character and presentation

- White/lilac long hair, purple eyes, twin dragon horns, wings, a dragon tail, and a white/lilac fantasy dress.
- Compact desktop footprint, nominal character height 100–180 px with user-controlled scaling.
- Runtime art uses seven approved transparent PNG state sprites on a shared canvas. A missing or damaged state sprite falls back to the approved idle image; if that also cannot load, the application shows a coherent built-in placeholder rather than fail.
- Personality is independent of the selected AI model. Future presets may include gentle, lively, quiet, tsundere, classic, and custom.

## 3. V0.1 scope

### Desktop pet shell

- Transparent, borderless window.
- Always on top by default, with a user setting and right-click toggle.
- Dragging moves the pet; the last valid position is restored on startup.
- Releasing a drag outside the character, across displays, or after focus changes must end the lifted state, briefly show `Happy`, and return to `Idle`.
- Mouse wheel changes scale within a safe range.
- Single click shows a tiny quick bar; double click opens or closes chat; right click opens the pet menu.
- Closing the window hides it to the tray. Exit is explicit.

### State and feedback

Required states: `Idle`, `Hover`, `Dragged`, `Thinking`, `Happy`, `Angry`, `Sleeping`.

- Hover wakes a sleeping pet and gives subtle visual feedback.
- Dragging uses a lifted pose/animation treatment and settles after release.
- AI requests enter `Thinking`; successful completion enters `Happy`; repeated rapid clicks enter `Angry`.
- Long inactivity can enter `Sleeping`.
- State changes must work even while placeholder art is active.
- Each state prefers its dedicated sprite; failure in one state must not disable the remaining state art.

### Tray and settings

- Tray commands: show, hide, settings, exit.
- A second launch signals the existing instance to show and activate; only one pet process remains.
- Settings: provider, model, base URL, API key, scale, always-on-top, start with Windows.
- First launch shows a small non-modal interaction guide beside the pet; acknowledging it is persisted and prevents it from appearing automatically again.
- Settings and pet position are stored in a portable `data/` folder beside the executable, so the application can remain entirely on the user's chosen drive.
- API keys are protected with Windows user-scoped encryption before writing to disk.
- EXE, pet window, and tray use the same dragon-girl application icon, with a system-icon fallback if the asset cannot load.

### Lightweight AI chat

- A compact bubble opens beside the pet; it is not a full-screen or full-height chat client.
- Sending shows a clear thinking state and then a concise answer or actionable configuration message.
- All calls go through `IAiProvider`.
- V0.1 includes an OpenAI-compatible transport suitable for OpenAI, DeepSeek, OpenRouter, Ollama, LM Studio, and custom compatible endpoints.
- Gemini and Anthropic are represented in the provider catalog for later native transports; choosing an unfinished transport must produce a friendly explanation rather than crash.

## 4. Out of V0.1 scope

Screenshot recognition, clipboard monitoring, file ingestion, document processing, reminders, system control, voice, long-term memory, window-edge physics, growth systems, outfits, games, and a plugin marketplace are deferred. Extension contracts may be added, but these features are not to be built yet.

## 5. Security and privacy

- No data is sent until the user submits chat content to a configured provider.
- No telemetry is added in V0.1.
- Destructive or high-impact Windows actions are prohibited without explicit confirmation infrastructure.
- The app runs as the current user and never requests administrator privileges.
- Crash reports are written locally beside the executable, exclude chat content, redact secrets, and retain only the newest ten files.

## 6. Acceptance checks

1. The solution builds on Windows with the .NET 8 desktop runtime.
2. The pet launches without a configured API key.
3. Interactions and every required state can be reached without crashing.
4. Position, scale, topmost choice, and provider settings survive restart.
5. Show/hide/settings/exit work from the tray.
6. A configured OpenAI-compatible endpoint can receive a prompt and return text.
7. All seven state sprites load at a common size with transparency, and a missing or damaged state falls back independently.
8. A second launch restores a hidden pet and exits without creating another long-running instance.
9. First-run guidance appears once and its completed state survives restart.
10. EXE, window, and tray display the formal icon; controlled crash diagnostics stay local, redact test secrets, and retain no more than ten files.
