# Reader → Firebase → App (live data bridge)

This connects your **Reader** (Telegram → threats → AI → analytics) to the
**Flutter app** through your Firebase Realtime Database. The Reader publishes the
same state JSON it builds for `map.html` to the RTDB node `state`; the app
streams that node and shows real threats, real AI analysis and real analytics.

```
Telegram ─▶ Reader (ThreatTracker + AiAdvisor) ─▶ RTDB /state ─▶ Flutter app
```

## What was added (code — already done)
- `src/Reader/FirebasePublisher.cs` — PUTs JSON to `<db>/state.json`.
- `MapWriter.BuildState()` returns the state JSON; `MapWriter.Write()` also
  publishes it when `MapWriter.Publisher` is set. All existing `Write()` calls
  publish automatically — no other Reader code changed.
- `Program.cs` — enables the publisher when `firebase_db_secret` is present.
- App: `MapDataController` streams the `state` node (already wired); a live
  snapshot replaces the demo scene.

## What you do (Firebase console — ~2 min, one time)

### 1. Give the Reader write access — a database secret
Firebase console → ⚙ **Project settings** → **Service accounts** →
**Database secrets** (legacy) → reveal/create → copy the secret.

Add it to `src/Reader/secrets.local.txt` (gitignored):
```
firebase_db_secret=PASTE_THE_SECRET_HERE
# optional, defaults to the project DB:
# firebase_db_url=https://waralarm-c8c1f-default-rtdb.europe-west1.firebasedatabase.app
```
The Reader writes with this secret, which bypasses the rules below.

> If "Database secrets" is hidden, click the ⋮/"legacy" hint on that page to
> show it. (A full service-account token also works, but the secret is simplest
> for a single server.)

### 2. Let the app read only the `state` node
Firebase console → **Realtime Database** → **Rules** → set and **Publish**:
```json
{
  "rules": {
    ".read": false,
    ".write": false,
    "state": { ".read": true, ".write": false }
  }
}
```
This exposes **only** the live-state node for public (guest) read, matching the
MVP's no-auth design. Everything else stays locked. The Reader still writes via
the secret.

## Run it
1. Start the Reader: `cd src/Reader && dotnet run`
   → it prints `🔥 Firebase ВКЛ → публікую стан у …/state.json`.
2. Open the app. It switches from **ДЕМО** to live automatically once data
   arrives; the map header shows "Живі цілі…". (You can also turn
   Settings → Дані → Демо-дані off.)

## Notes
- Publishing is fire-and-forget and never blocks the Reader; if Firebase is
  unreachable it logs a throttled warning and keeps going.
- No secrets are committed — `secrets.local.txt` is gitignored.
- Cost: RTDB writes are tiny JSON documents on each update; well within the free
  tier for this usage.
