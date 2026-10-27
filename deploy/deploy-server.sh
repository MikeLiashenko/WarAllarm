#!/usr/bin/env bash
# ── WarAlarm · перенести Reader на сервер однією командою (з Git Bash на ПК) ──
#   deploy/deploy-server.sh user@IP             перший раз: програма + секрети + сесія Telegram
#   deploy/deploy-server.sh user@IP --code      далі: лише оновити програму (сесію на сервері не чіпати)
#
# Програму збираємо ТУТ (dotnet publish -r linux-x64): слабкий сервер (e2-micro, 1 ГБ) лише
# пакує готову папку в образ (Dockerfile.prebuilt). Користувач на сервері — з sudo.
# Важливо: Reader із тією самою сесією Telegram НЕ можна тримати в двох місцях одночасно
# (Telegram може анулювати сесію). Тож перед стартом на сервері локальний Reader зупиняється.
set -euo pipefail
HOST="${1:?Вкажи сервер: deploy/deploy-server.sh user@IP}"
MODE="${2:-}"
KEY="$HOME/.ssh/waralarm_server"
SSH=(ssh -i "$KEY" -o StrictHostKeyChecking=accept-new "$HOST")
cd "$(dirname "$0")/.."

tmp="$(mktemp -d)"
echo "🔨 Збираю програму для Linux …"
dotnet publish src/Reader/WarAlarm.Reader.csproj -c Release -r linux-x64 --self-contained false -o "$tmp/pkg/app" >/dev/null
cp Dockerfile.prebuilt docker-compose.yml "$tmp/pkg/"
echo "WARALARM_DOCKERFILE=Dockerfile.prebuilt" > "$tmp/pkg/.env"
tar czf "$tmp/pkg.tgz" -C "$tmp/pkg" .

if [ "$MODE" != "--code" ]; then
  echo "⏹ Зупиняю локальний Reader (start.bat + процес), щоб сесія Telegram була лише на сервері …"
  powershell -NoProfile -Command "Get-CimInstance Win32_Process | Where-Object { \$_.CommandLine -like '*start.bat*' -and \$_.Name -ne 'powershell.exe' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force }; Get-Process WarAlarm.Reader -ErrorAction SilentlyContinue | Stop-Process -Force" || true
  sleep 3
  echo "🔑 Секрети, сесія, підписники бота …"
  mkdir "$tmp/data"
  for f in secrets.local.txt settings.local.txt WTelegram.session updates.state bot_subscribers.json alarms.csv boom_feedback.csv; do
    [ -f "src/Reader/$f" ] && cp "src/Reader/$f" "$tmp/data/"
  done
  tar czf "$tmp/data.tgz" -C "$tmp/data" .
fi

echo "⬆ Копіюю на $HOST …"
scp -q -i "$KEY" -o StrictHostKeyChecking=accept-new "$tmp"/*.tgz "$HOST:/tmp/"

echo "🚀 Сервер: образ, старт …"
"${SSH[@]}" "MODE='$MODE' bash -s" <<'REMOTE'
set -e
command -v docker >/dev/null || { curl -fsSL https://get.docker.com | sudo sh; }
sudo mkdir -p /opt/waralarm/data && cd /opt/waralarm
sudo rm -rf app && sudo tar xzf /tmp/pkg.tgz
if [ "$MODE" != "--code" ] && [ -f /tmp/data.tgz ]; then sudo tar xzf /tmp/data.tgz -C data; fi
sudo docker compose up -d --build
echo "⏳ Чекаю на вхід у Telegram …"
for i in $(seq 1 40); do sudo docker compose logs 2>/dev/null | grep -q "Вошли как" && break; sleep 3; done
sudo docker compose logs --tail 25
rm -f /tmp/pkg.tgz /tmp/data.tgz
REMOTE
rm -rf "$tmp"
echo "✅ Готово. Логи:  ssh -i ~/.ssh/waralarm_server $HOST 'cd /opt/waralarm && sudo docker compose logs -f'"
