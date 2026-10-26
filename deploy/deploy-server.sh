#!/usr/bin/env bash
# ── WarAlarm · перенести Reader на сервер однією командою (з Git Bash на ПК) ──
#   deploy/deploy-server.sh root@IP             перший раз: код + секрети + сесія Telegram
#   deploy/deploy-server.sh root@IP --code      далі: лише оновити код (сесію на сервері не чіпати)
#
# Важливо: Reader із тією самою сесією Telegram НЕ можна тримати в двох місцях одночасно
# (Telegram може анулювати сесію). Тож перед стартом на сервері локальний Reader зупиняється.
set -euo pipefail
HOST="${1:?Вкажи сервер: deploy/deploy-server.sh root@IP}"
MODE="${2:-}"
KEY="$HOME/.ssh/waralarm_server"
SSH=(ssh -i "$KEY" -o StrictHostKeyChecking=accept-new "$HOST")
cd "$(dirname "$0")/.."

tmp="$(mktemp -d)"
echo "📦 Код (git HEAD) …"
git archive --format=tar.gz -o "$tmp/code.tgz" HEAD -- src Dockerfile docker-compose.yml .dockerignore deploy

if [ "$MODE" != "--code" ]; then
  echo "⏹ Зупиняю локальний Reader (start.bat + процес), щоб сесія Telegram була лише на сервері …"
  powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { \$_.CommandLine -match 'start\.bat' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force }; Get-Process WarAlarm.Reader -ErrorAction SilentlyContinue | Stop-Process -Force" || true
  sleep 3
  echo "🔑 Секрети, сесія, підписники бота …"
  mkdir "$tmp/data"
  for f in secrets.local.txt settings.local.txt WTelegram.session bot_subscribers.json alarms.csv; do
    [ -f "src/Reader/$f" ] && cp "src/Reader/$f" "$tmp/data/"
  done
  tar czf "$tmp/data.tgz" -C "$tmp/data" .
fi

echo "⬆ Копіюю на $HOST …"
scp -i "$KEY" -o StrictHostKeyChecking=accept-new "$tmp"/*.tgz "$HOST:/tmp/"

echo "🚀 Сервер: Docker, збірка, старт …"
"${SSH[@]}" "MODE='$MODE' bash -s" <<'REMOTE'
set -e
command -v docker >/dev/null || { curl -fsSL https://get.docker.com | sh; }
mkdir -p /opt/waralarm/data && cd /opt/waralarm
tar xzf /tmp/code.tgz
if [ "$MODE" != "--code" ] && [ -f /tmp/data.tgz ]; then tar xzf /tmp/data.tgz -C data; fi
docker compose up -d --build
echo "⏳ Чекаю на вхід у Telegram …"
for i in $(seq 1 40); do docker compose logs 2>/dev/null | grep -q "Вошли как" && break; sleep 3; done
docker compose logs --tail 25
REMOTE
rm -rf "$tmp"
echo "✅ Готово. Логи:  ssh -i ~/.ssh/waralarm_server $HOST 'cd /opt/waralarm && docker compose logs -f'"
