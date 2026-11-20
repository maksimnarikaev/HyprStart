# HyprStart

A **Hyprland-inspired launcher for Windows 11**. Press a hotkey and a translucent overlay slides
out with your app grid, frequent shortcuts, a search field, media controls and a built-in AI chat.
Native WPF, no Electron, no background telemetry.

![HyprStart overview](assets/overview.png)

---

## Features

- **Hyprland-inspired dashboard** — a single frameless overlay window with rounded translucent
  panels, a pinned app grid, a frequent-apps column and a media player strip. The whole layout is
  laid out dynamically so pinned tiles reflow (and the chat panel can collapse) without a resize.
- **Dynamic wallpaper palette extraction** — the accent colour is sampled from the current desktop
  wallpaper and pushed into every panel as a tinted glass background. Near-black and monochrome
  wallpapers fall back to the bundled `sakura-pink` palette; the extractor samples ~576 pixels in
  a single pass and is covered by unit tests.
- **Built-in two-pass RAG mini-chat** — ask a question in the chat panel. When a web lookup is
  useful the `WebSearch`/`WebPageExtractor` pipeline runs two passes: first it strips navigation,
  scripts and cookie banners, then it keeps only fact-carrying paragraphs before they are handed
  to the model as context. Use `/web <query>` to force a lookup.
- **EN/RU localization** — every visible string lives in `Resources/Strings.en.xaml` /
  `Strings.ru.xaml` (135 keys each). The language can be switched in settings without a restart.
- **Zero-telemetry native WPF** — no analytics, no crash reporting, no updater pings. The only
  network calls are the ones you configure: your LLM provider, the optional web lookup and
  Open-Meteo for the weather tile.
- **Hotkey capture that actually works** — combinations are recorded through a `WH_KEYBOARD_LL`
  hook, so `Alt+Q`, `Ctrl+Alt+K` and `Win`-combinations are captured instead of opening the system
  menu or the Start screen.

---

## Architecture

The solution is split so the interesting logic is testable without a UI thread.

| Project | Purpose |
| --- | --- |
| `src/HyprStart.Core` | Platform-independent logic: layout calculation, wallpaper colour extraction, settings storage, chat client, web search/extraction, action dispatch, input state machine. |
| `src/HyprStart.App` | WPF shell (`net8.0-windows10.0.19041.0`): overlay window, settings window, theming, interop (`RegisterHotKey`, `WH_KEYBOARD_LL`, tray icon, GSMTC media sessions). |
| `tests/HyprStart.Core.Tests` | **409 tests** covering core behaviour: geometry and reflow, colour extraction edge cases, settings migration, chat parsing, SSE streaming, URL handling. |

```
HyprStart.Core
├── Actions        command dispatch
├── Chat           chat client, SSE parser, web search, page extractor
├── Input          key state machine
├── Layout         panel geometry and tile reflow
├── Library        app index and icons
├── Settings       settings model, storage and migration
├── Storage        file paths and atomic writes
└── Theming        wallpaper palette extraction
```

**Stack:** .NET 8, WPF, C# 12 (`Nullable` enabled). No third-party runtime dependencies in the app.

---

## Hotkeys

| Shortcut | Action |
| --- | --- |
| `Alt+Space` | Open / close the overlay (configurable in settings) |
| `↑` / `↓` | Move the selection in search results |
| `Enter` | Launch the selected app |
| `Alt+1` … `Alt+9` | Launch pinned tile 1–9 |
| `Tab` | Jump from the app list to the chat input |
| `Ctrl+,` | Open settings |
| `Ctrl+L` | Clear the chat |
| `F5` | Rebuild the app index in the background |
| `Esc` | Close the overlay |
| RMB on an app | Pin / unpin |

The global open-combination is recorded in **Settings → Hotkey**: press the field, then the
combination. A modifier is required; `Esc` cancels the recording.

---

## Configuring an LLM provider

HyprStart talks to any OpenAI-compatible `/chat/completions` endpoint. Open **Settings** (`Ctrl+,`)
and fill in:

| Field | Example | Notes |
| --- | --- | --- |
| **Base URL** | `https://api.deepseek.com` | Scheme is optional; `/chat/completions` is appended automatically. |
| **API Key** | `sk-…` | Stored in `settings.json` and **masked by default** — press the eye button to reveal it. |
| **Model** | `deepseek-chat` | Any model the endpoint accepts. |

Tested endpoints:

| Provider | Base URL | Model example |
| --- | --- | --- |
| DeepSeek | `https://api.deepseek.com` | `deepseek-chat` |
| OpenAI | `https://api.openai.com/v1` | `gpt-4o-mini` |
| OpenRouter | `https://openrouter.ai/api/v1` | `anthropic/claude-3.5-sonnet` |
| Groq | `https://api.groq.com/openai/v1` | `llama-3.3-70b-versatile` |
| Ollama (local) | `http://localhost:11434/v1` | `llama3.1` |

> **The API key is read from settings only.** HyprStart does **not** pick up `DEEPSEEK_API_KEY`
> (or any other variable) from the environment, so it cannot silently borrow a key that belongs to
> another tool on your machine.

Settings live in `%LOCALAPPDATA%\HyprStart\settings.json` and are not part of this repository.

---

## Build from source

Requires the **.NET 8 SDK** on Windows 10 1809+ / Windows 11.

```powershell
git clone https://github.com/maksimnarikaev/HyprStart.git
cd HyprStart

dotnet build HyprStart.sln -c Release
dotnet test  tests\HyprStart.Core.Tests -c Release

# run it
dotnet run --project src\HyprStart.App\HyprStart.App.csproj -c Release
```

### Portable release

`scripts/build-release.ps1` produces a self-contained single-file build and packs it into a zip:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-release.ps1 -Clean
```

Output: `dist\HyprStart-win-x64.zip` (`dist/` is git-ignored). The archive is portable — unpack it
anywhere and run `HyprStart.exe`; no .NET installation is required.

---

## CI/CD

`.github/workflows/ci.yml` runs on every push and pull request to `main`:

1. `dotnet build HyprStart.sln -c Release -warnaserror` on `windows-latest`
2. `dotnet test tests\HyprStart.Core.Tests -c Release`

Pushing a tag (`v*`) additionally publishes the self-contained `win-x64` single-file build, packs
it into `HyprStart-win-x64.zip` and attaches it to a GitHub Release.

```powershell
git tag v1.0.0
git push origin v1.0.0
```

---

## Screenshots

Drop images into [`assets/`](assets/) using the names below — the README picks them up as-is.

| File | What to capture |
| --- | --- |
| `assets/overview.png` | The full overlay: pinned grid + frequent apps + media strip |
| `assets/chat.png` | The MINI AI panel with a web-backed answer |
| `assets/settings.png` | The settings window with the masked API key field |

---

## License

No license file is included in this repository yet. Until one is added, the default
copyright rules apply — open an issue if you need a specific license for your use case.
