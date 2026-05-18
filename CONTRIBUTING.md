# Contributing

Thanks for considering a contribution to Mini Wallpaper.

## Before you start

Mini Wallpaper is intentionally small. The project favors:

- low runtime overhead;
- simple behavior that is easy to reason about;
- local-first media handling;
- focused features over broad platform ambitions.

If a proposed feature adds meaningful background work, dependencies, or UI complexity, please explain the tradeoff clearly in the issue or pull request.

## Local workflow

```powershell
powershell -ExecutionPolicy Bypass -File .\native-wpf\build.ps1
```

For install-path testing:

```powershell
powershell -ExecutionPolicy Bypass -File .\native-wpf\install.ps1
```

## Pull requests

Please include:

- what changed;
- why it changed;
- any user-visible behavior change;
- how you tested it.

Small, focused pull requests are preferred.

