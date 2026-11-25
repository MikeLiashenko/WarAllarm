# WarAlarm — правила для Claude

- Готові виправлення одразу пушити в `main` (fast-forward, без питань): сервер (`server-latest`),
  сайт і APK збираються лише з `main`. Після пушу дочекатися збірок і нагадати виконати на сервері:
  `curl -fsSL https://github.com/MikeLiashenko/WarAllarm/releases/download/server-latest/server-update.sh | bash`
- Журнал сервера (`journal/*.jsonl`) недоступний — ніч відтворюється з публічних стрічок `t.me/s/<канал>`
  у той самий формат і проганяється офлайн: `replay`, `audit`, `ballreplay`, `fcast`.
