# WarAlarm — Flutter App

Cross-platform air-raid / threat monitoring client for **Android (.apk)** and
**Windows Desktop (.exe)**, connecting to the WarAlarm Firebase Realtime
Database.

<p>
  <img alt="Flutter" src="https://img.shields.io/badge/Flutter-3.44-blue">
  <img alt="Platforms" src="https://img.shields.io/badge/Platforms-Android%20%7C%20Windows-3B9EFF">
</p>

## Design system

| Token        | Value                                             |
| ------------ | ------------------------------------------------- |
| Background   | Crisp white / very light blue                     |
| Primary      | Light blue `#3B9EFF`                              |
| Threat       | Red `#F4433D` — **reserved strictly for alerts**  |
| Surfaces     | Elegant frosted glass (`BackdropFilter` + white)  |
| Navigation   | Fixed left sidebar with an animated active tab    |

## Screens

1. **Now** — prominent animated threat circle (blue calm / pulsing red alert),
   a light-blue analytics chart above a live event feed.
2. **Map** — custom-painted Ukraine threat map ported from the website
   (`src/Reader/MapWriter.cs`): real country silhouette, tiered cities, detailed
   **rotated target silhouettes** (Shahed / cruise / ballistic / KAB / aircraft),
   trajectories, **AI course prediction** (`→ city?`), ×N clustering, pulsing
   home marker, pan/zoom and dead-reckoning target movement.
3. **My Places** — add / pin / manage monitored regions (persisted locally).
4. **Settings** — custom native preference widgets (notifications, display).
5. **Chat** — community messaging interface.
6. **AI** — assistant with a **multi-session** history (switch / new / delete).
7. **API Keys** — generate keys, manage them, copy Python / JS / cURL snippets.

## Architecture

Modular, `Provider`-based:

```
lib/
├── main.dart                 # bootstrap (LocalStore + RtdbService)
├── app.dart                  # MultiProvider + MaterialApp
├── firebase_config.dart      # RTDB URL, project ids, node paths
├── theme/                    # colors + ThemeData
├── models/                   # AlertEvent, ChatMessage, SavedPlace, AiSession, ApiKey
├── services/
│   ├── rtdb_service.dart     # RTDB REST + SSE streaming client
│   └── local_store.dart      # SharedPreferences wrapper
├── controllers/              # ChangeNotifier per feature
├── data/                     # ukraine_path.dart (outline) + ukraine_geo.dart (cities, silhouettes)
├── widgets/                  # GlassContainer, AppSidebar, ThreatCircle, UkraineMap, charts
└── screens/                  # one file per sidebar tab + HomeShell
```

The map is a faithful Flutter/`CustomPainter` port of the website's SVG map: the
Ukraine outline path and target silhouettes are lifted verbatim from
`MapWriter.cs`, projected with the same equirectangular math, and fed from the
RTDB `map` node (`{home, homeName, threats[]}`) with a demo scene as fallback.

## Why REST + SSE instead of the `firebase_database` plugin

The native `firebase_database` FlutterFire plugin **has no Windows desktop
implementation**, so using it would break `flutter build windows`. Instead,
[`RtdbService`](lib/services/rtdb_service.dart) talks directly to the Realtime
Database's **public REST + Server-Sent-Events streaming API**
(`<url>/<path>.json` with `Accept: text/event-stream`). This behaves exactly
like `ref().onValue` — pushing a fresh snapshot on every change — and runs
**identically on Android and Windows** with no per-platform native SDK. It
matches the MVP's "no auth / public nodes" requirement.

Expected RTDB layout (all optional — the UI falls back to demo data):

```jsonc
{
  "status": { "threat": false, "label": "All clear", "region": "Kharkiv" },
  "feed":   { "<pushId>": { "title": "...", "message": "...", "region": "...", "threat": true, "timestamp": 1700000000000 } },
  "chat":   { "<pushId>": { "author": "Olena", "text": "...", "timestamp": 1700000000000 } }
}
```

## Build & run

```bash
cd app
flutter pub get

# Run
flutter run -d windows          # Windows desktop
flutter run -d <android-device> # Android

# Release artifacts
flutter build windows --release # build/windows/x64/runner/Release/waralarm.exe
flutter build apk --release     # build/app/outputs/flutter-apk/app-release.apk
```

Requirements: Flutter 3.44+, and for Windows the "Desktop development with C++"
Visual Studio workload.

## Notes / next steps

- The AI assistant uses a local rule-based responder so the screen works
  offline; swap `AiController._generateReply` for a real API call (e.g. the
  Claude API) — the session/history plumbing is unchanged.
- API keys are generated and stored locally for the MVP; wire them to a backend
  to enforce them server-side.
