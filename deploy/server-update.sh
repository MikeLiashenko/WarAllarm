#!/usr/bin/env bash
# ── WarAlarm · оновити сервер БЕЗ ПК (запускати на самому сервері) ──────────────
#   curl -fsSL https://github.com/MikeLiashenko/WarAllarm/releases/download/server-latest/server-update.sh | bash
# Бере готову програму з релізу server-latest (її збирає GitHub Actions), кладе в /opt/waralarm
# і перезапускає контейнер. Папку /opt/waralarm/data (секрети, сесія Telegram) НЕ чіпає.
set -euo pipefail
URL="https://github.com/MikeLiashenko/WarAllarm/releases/download/server-latest/waralarm-server.tgz"
DIR=/opt/waralarm

command -v docker >/dev/null || { echo "🐳 Ставлю Docker …"; curl -fsSL https://get.docker.com | sudo sh; }
sudo mkdir -p "$DIR/data"
cd "$DIR"
[ -f data/WTelegram.session ] || echo "⚠ Немає $DIR/data/WTelegram.session — Telegram попросить код входу."
[ -f data/secrets.local.txt ] || echo "⚠ Немає $DIR/data/secrets.local.txt — без нього Reader не стартує."

echo "⬇ Завантажую свіжу збірку …"
curl -fL --retry 4 "$URL" -o /tmp/waralarm-server.tgz
sudo rm -rf app
sudo tar xzf /tmp/waralarm-server.tgz
rm -f /tmp/waralarm-server.tgz
echo "📦 Збірка: $(cat app/BUILD_COMMIT 2>/dev/null || echo '?')"

echo "🚀 Перезапуск …"
# Ручні «docker compose run» тримають сесію Telegram — прибрати, інакше новий контейнер не стартує.
sudo docker ps -aq --filter label=com.docker.compose.oneoff=True | xargs -r sudo docker rm -f
START="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
sudo docker compose up -d --build --force-recreate
sudo docker image prune -f >/dev/null 2>&1 || true

# Чекаємо вхід. Якщо Telegram просить код/пароль — питаємо тут і кладемо у data/ (програма його підхопить).
# Якщо SSH обірветься — не страшно: контейнер чекає 30 хв, код можна дописати пізніше:
#   echo КОД | sudo tee /opt/waralarm/data/telegram_code.txt
echo "⏳ Чекаю на вхід у Telegram (до 10 хв; FLOOD_WAIT програма перечікує сама) …"
asked_code=0; asked_pw=0
for i in $(seq 1 200); do
  L="$(sudo docker compose logs --no-log-prefix --since "$START" 2>/dev/null || true)"
  if grep -q "Вошли как" <<<"$L"; then break; fi
  if grep -q "telegram_password.txt" <<<"$L" && [ $asked_pw = 0 ]; then
    read -rsp "🔐 Пароль 2FA Telegram: " pw </dev/tty; echo
    printf '%s' "$pw" | sudo tee data/telegram_password.txt >/dev/null; asked_pw=1
  elif [ "$(grep -c "telegram_code.txt" <<<"$L")" -gt "$asked_code" ]; then
    echo "📲 Telegram надіслав код входу (у застосунку Telegram, чат «Telegram»)."
    read -rp "🔢 Введи код: " code </dev/tty
    printf '%s' "$code" | sudo tee data/telegram_code.txt >/dev/null; asked_code=$((asked_code+1))
  fi
  sleep 3
done
sudo docker compose logs --tail 15
if sudo docker compose logs --since "$START" 2>/dev/null | grep -q "Вошли как"; then
  echo "✅ Готово, Reader працює. Логи:  cd $DIR && sudo docker compose logs -f"
else
  echo "⚠ Входу ще немає. Подивись логи вище або:  cd $DIR && sudo docker compose logs --tail 30"
fi
