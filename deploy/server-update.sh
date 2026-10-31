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
sudo docker compose up -d --build
sudo docker image prune -f >/dev/null 2>&1 || true

echo "⏳ Чекаю на вхід у Telegram …"
for i in $(seq 1 40); do sudo docker compose logs 2>/dev/null | grep -q "Вошли как" && break; sleep 3; done
sudo docker compose logs --tail 25
echo "✅ Готово. Логи:  cd $DIR && sudo docker compose logs -f"
