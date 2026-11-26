# Трейлер WarAlarm (реліз 06.12.2026)

**Основний — горизонтальний 1920×1080, 60 с:** `trailer_h.html`, `mapframes_h.js` (кадри карти в `maph/`), `render_h.js` (кадри в `outh/`). Кроки ті самі, що нижче.

## Вертикальний 1080×1920, 35 с

Вертикальне відео 1080×1920, 35 с, 30 кадрів/с — для Telegram, Reels, TikTok.

1. Кадри живої карти (сайт у демо-режимі):
   `python3 -m http.server 8765` у `site/`, далі `node mapframes.js <тека>` → `<тека>/map/0000…0149.png`.
2. Скопіювати `trailer.html` і `logo.svg` у ту саму теку, відрендерити кадри: `node render.js <тека> full`
   (`preview` — 10 ключових кадрів для перевірки).
3. Зібрати: `ffmpeg -framerate 30 -i out/f%05d.png -i audio.wav -c:v libx264 -crf 19 -pix_fmt yuv420p -c:a aac -shortest WarAlarm_trailer.mp4`.

Дата, час і назви — у `trailer.html` (сцена S6). Звук синтезований (гул, удари на зміні сцен), без сирени.
