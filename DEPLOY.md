# WarAlarm · сервер 24/7 🇺🇦

Чтобы карта и оповещения работали **всегда** (даже когда твой ПК выключен или пропал
свет дома) — программа должна крутиться на **сервере в облаке**. Он не зависит от твоих
блэкаутов: во время атаки твой дом может остаться без света и интернета, а сервер в
дата-центре продолжит слушать каналы и слать push тебе на телефон.

> ⚠️ Аккаунт хостинга и оплату оформляй **вместе с папой** (там 18+ и нужна карта).

---

## Куда ставить (варианты хоста)

| Хост | Цена | Заметки |
|---|---|---|
| **Oracle Cloud — Always Free** | 0 ₴ | ARM-виртуалка бесплатно навсегда; при регистрации нужна карта для проверки (списаний нет). Лучший бесплатный вариант. |
| **Hetzner / Contabo VPS** | ~150–200 ₴/міс | Дёшево и надёжно, Германия. |
| **Домашний мини-ПК / Raspberry Pi** | 0 ₴ | НО во время атаки часто нет света → сервер погаснет. Только с ИБП. |

Нужен любой **Linux (Ubuntu 22.04+)** с 1 ГБ RAM.

---

## Запуск через Docker (проще всего)

На сервере (Ubuntu):

```bash
# 1) поставить Docker
curl -fsSL https://get.docker.com | sudo sh

# 2) забрать код
git clone https://github.com/MikeLiashenko/WarAllarm.git
cd WarAllarm

# 3) создать папку data и положить туда СВОИ файлы (их нет в репозитории!)
mkdir -p data
#   скопируй с ПК в ./data:
#     secrets.local.txt   (api_id, api_hash, phone, ntfy_topic, ai_api_key)
#     settings.local.txt  (my_district, watch_*, ua_channels, my_lat/my_lon)
#     WTelegram.session   (файл ВХОДА в Telegram — берётся из src/Reader после
#                          того как ты хоть раз залогинился локально; тогда код
#                          подтверждения на сервере вводить НЕ нужно)

# 4) запустить (соберётся и будет работать в фоне, поднимется после ребута)
sudo docker compose up -d --build

# логи / проверка
sudo docker compose logs -f
```

**Копирование файлов с Windows на сервер** (в PowerShell на ПК):
```powershell
scp src\Reader\secrets.local.txt src\Reader\settings.local.txt src\Reader\WTelegram.session user@СЕРВЕР:~/WarAllarm/data/
```

Если сессию не копировал — при первом старте Telegram спросит код. Запусти один раз
интерактивно, введи код, потом сервис сам сохранит `WTelegram.session`:
```bash
sudo docker compose run --rm waralarm     # ввести код, затем Ctrl+C
sudo docker compose up -d                  # дальше в фоне
```

---

## Без Docker (systemd)

```bash
sudo apt update && sudo apt install -y dotnet-sdk-9.0 git
git clone https://github.com/MikeLiashenko/WarAllarm.git
cd WarAllarm/src/Reader
dotnet publish -c Release -o /opt/waralarm/app
sudo mkdir -p /opt/waralarm/data
# скопируй secrets.local.txt / settings.local.txt / WTelegram.session в /opt/waralarm/data
sudo useradd -r waralarm; sudo chown -R waralarm /opt/waralarm
sudo cp deploy/waralarm.service /etc/systemd/system/
sudo systemctl daemon-reload && sudo systemctl enable --now waralarm
journalctl -u waralarm -f
```

---

## Безопасность

- `secrets.local.txt` и `WTelegram.session` = **доступ к твоему Telegram**. Держи сервер
  под паролем/ключом SSH, никому файлы не давай, в репозиторий они НЕ попадают (`.gitignore`).
- Push на телефон работает через **ntfy** — ничего дополнительно на сервере не нужно.
- Звук на сервере не проигрывается (и не нужен) — тревога приходит на телефон.

## Смотреть карту с сервера (по желанию)

Раскомментируй блок `map:` в `docker-compose.yml` — тогда карта будет на
`http://СЕРВЕР:8080/map.html`.
