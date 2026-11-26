# Трейлер WarAlarm (реліз 06.12.2026)

**Основний — горизонтальний 1920×1080, 60 с** (`trailer_h.html`):
1. Справжня жива карта: `python3 -m http.server 8765` у `site/`, далі `node liveframes.js <тека>` (знімок сайту як є → `live/`)
   і `node liveframes_c.js <тека>` (та сама карта без панелей → `livec/`). Знімати під час атаки, коли на карті є цілі.
2. У ту саму теку: `trailer_h.html`, `render_h.js`, іконка `icon.png` (= `app/assets/icon/waralarm_1024.png`).
   `node render_h.js <тека> preview` — 12 ключових кадрів, `full` — 1800 кадрів у `outh/`.
3. Музика — Kevin MacLeod, incompetech.com, ліцензія CC BY 4.0 (у пості обовʼязково вказати автора):
   «Floating Cities» з 50-ї секунди (основна) або «Dreams Become Real» з 34-ї:
   `ffmpeg -ss 50 -t 60 -i Floating_Cities.mp3 -af "afade=t=in:d=0.8,afade=t=out:st=57:d=3,loudnorm=I=-16:TP=-1.5" -ar 48000 -ac 2 music.wav`.
   Запасний варіант без авторських прав — синтезована `python3 music.py music.wav`.
4. `ffmpeg -framerate 30 -i outh/f%05d.png -i music.wav -c:v libx264 -crf 19 -pix_fmt yuv420p -c:a aac -b:a 192k -shortest WarAlarm_trailer_1080p.mp4`

Дата, час, назви — сцена S6 у `trailer_h.html`; підпис під знімком карти — `#shotcap`.

## Вертикальний 1080×1920, 35 с

Вертикальне відео 1080×1920, 35 с, 30 кадрів/с — для Telegram, Reels, TikTok.

1. Кадри живої карти (сайт у демо-режимі):
   `python3 -m http.server 8765` у `site/`, далі `node mapframes.js <тека>` → `<тека>/map/0000…0149.png`.
2. Скопіювати `trailer.html` і `logo.svg` у ту саму теку, відрендерити кадри: `node render.js <тека> full`
   (`preview` — 10 ключових кадрів для перевірки).
3. Зібрати: `ffmpeg -framerate 30 -i out/f%05d.png -i audio.wav -c:v libx264 -crf 19 -pix_fmt yuv420p -c:a aac -shortest WarAlarm_trailer.mp4`.

Дата, час і назви — у `trailer.html` (сцена S6). Звук синтезований (гул, удари на зміні сцен), без сирени.
