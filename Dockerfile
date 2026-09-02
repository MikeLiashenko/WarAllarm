# ── WarAlarm · образ для сервера 24/7 ────────────────────────────────────────
# Сборка внутри образа, запуск на рантайме .NET. Звук на сервере не нужен —
# оповещение идёт на телефон через ntfy (push). Карта пишется в /data/map.html.

# 1) сборка
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/Reader/WarAlarm.Reader.csproj src/Reader/
RUN dotnet restore src/Reader/WarAlarm.Reader.csproj
COPY . .
RUN dotnet publish src/Reader/WarAlarm.Reader.csproj -c Release -o /app --no-restore

# 2) запуск
FROM mcr.microsoft.com/dotnet/runtime:9.0
COPY --from=build /app /app
# Рабочая папка = /data: сюда монтируются secrets.local.txt, settings.local.txt,
# WTelegram.session; сюда же пишутся map.html, alarms.csv, обновления сессии.
WORKDIR /data
ENTRYPOINT ["dotnet", "/app/WarAlarm.Reader.dll"]
