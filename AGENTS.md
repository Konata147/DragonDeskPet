# DragonDeskPet development guide

## Product

This is a Windows-native Q-version dragon-girl desktop pet. The pet itself is the primary interface; it must not drift into a large conventional chat application.

Visual canon:

- `references/character-original.jpg` defines the white/lilac hair, twin horns, wings, dragon tail, purple eyes, and white-lilac dress.
- `references/chibi-animation-reference.jpg` defines the desired chibi proportions and animation breadth.
- `references/desktop-pet-reference.jpg` defines the small, unobtrusive desktop scale.
- Do not replace or materially redesign these identity traits without the user's approval.
- Files under `references/` are reference-only. Runtime-ready art belongs under `assets/`.

## Engineering rules

1. Preserve the separation between UI, pet state/animation, AI providers, configuration, and Windows services.
2. Never couple the character personality or state machine to one AI vendor.
3. All model access must go through `IAiProvider` and `AiProviderFactory`.
4. Local data stays local unless a user action explicitly sends it to a configured provider.
5. Protect stored API keys with Windows user-scoped encryption.
6. High-impact system actions require explicit confirmation. Do not add autonomous destructive actions.
7. Keep normal desktop presence lightweight, quiet, transparent, and compact.
8. V0.1 may reserve interfaces for later features but must not implement V0.2/V0.3 scope prematurely.
9. Build after each substantial module and fix errors before continuing.
10. Keep the app usable without an API key: pet interactions, settings, tray, and a clear offline response must still work.

## V0.1 acceptance baseline

- Transparent borderless Windows pet window.
- Toggleable always-on-top behavior.
- Drag, hover, single-click, double-click, right-click, and mouse-wheel interactions.
- `Idle`, `Hover`, `Dragged`, `Thinking`, `Happy`, `Angry`, and `Sleeping` states.
- Tray actions for show, hide, settings, and exit.
- Persisted position and scale.
- Lightweight floating chat bubble.
- Provider-neutral AI architecture and locally stored configuration.
