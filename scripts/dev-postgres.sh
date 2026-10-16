#!/usr/bin/env bash
# Локальный dev-кластер PostgreSQL для DrawingFlow (без Docker и без sudo).
# Использует бинарники EDB PostgreSQL 16, данные хранит в .local/pgdata, порт 5433.
# Команды: start | stop | status | psql | reset
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PG_BIN="${PG_BIN:-/Library/PostgreSQL/16/bin}"
PGDATA="${PGDATA:-$REPO_ROOT/.local/pgdata}"
PGPORT="${PGPORT:-5433}"
PGLOG="$REPO_ROOT/.local/postgres.log"
DB_NAME="drawingflow"
DB_USER="drawingflow"
DB_PASSWORD="drawingflow"   # только для локальной разработки

if [[ ! -x "$PG_BIN/pg_ctl" ]]; then
  echo "PostgreSQL не найден в $PG_BIN. Укажите PG_BIN=/путь/к/bin." >&2
  exit 1
fi

init_cluster() {
  if [[ ! -f "$PGDATA/PG_VERSION" ]]; then
    echo "Инициализация кластера в $PGDATA ..."
    mkdir -p "$PGDATA"
    local pwfile
    pwfile="$(mktemp)"
    echo "$DB_PASSWORD" > "$pwfile"
    # Локаль должна поддерживать кириллицу: иначе lower()/ILIKE и сортировка работают только для латиницы.
    local pg_locale="C.UTF-8"
    if locale -a 2>/dev/null | grep -qi '^ru_RU.UTF-8$'; then pg_locale="ru_RU.UTF-8";
    elif locale -a 2>/dev/null | grep -qi '^en_US.UTF-8$'; then pg_locale="en_US.UTF-8"; fi
    echo "Локаль кластера: $pg_locale"
    "$PG_BIN/initdb" -D "$PGDATA" -U postgres --auth=scram-sha-256 --pwfile="$pwfile" -E UTF8 --locale="$pg_locale" >/dev/null
    rm -f "$pwfile"
    {
      echo "port = $PGPORT"
      echo "listen_addresses = 'localhost'"
      echo "unix_socket_directories = '$PGDATA'"
      echo "max_connections = 100"
    } >> "$PGDATA/postgresql.conf"
  fi
}

ensure_role_and_db() {
  local psql="$PG_BIN/psql -h localhost -p $PGPORT -U postgres -v ON_ERROR_STOP=1 -qtA"
  export PGPASSWORD="$DB_PASSWORD"
  if [[ "$($psql -c "select 1 from pg_roles where rolname='$DB_USER'")" != "1" ]]; then
    $psql -c "create role $DB_USER with login createdb password '$DB_PASSWORD'"
  fi
  if [[ "$($psql -c "select 1 from pg_database where datname='$DB_NAME'")" != "1" ]]; then
    $psql -c "create database $DB_NAME owner $DB_USER"
  fi
  unset PGPASSWORD
}

case "${1:-}" in
  start)
    init_cluster
    if "$PG_BIN/pg_ctl" -D "$PGDATA" status >/dev/null 2>&1; then
      echo "Кластер уже запущен (порт $PGPORT)."
    else
      "$PG_BIN/pg_ctl" -D "$PGDATA" -l "$PGLOG" -w start
    fi
    ensure_role_and_db
    echo "Готово: Host=localhost;Port=$PGPORT;Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASSWORD"
    ;;
  stop)
    "$PG_BIN/pg_ctl" -D "$PGDATA" -m fast -w stop
    ;;
  status)
    "$PG_BIN/pg_ctl" -D "$PGDATA" status
    ;;
  psql)
    shift
    PGPASSWORD="$DB_PASSWORD" exec "$PG_BIN/psql" -h localhost -p "$PGPORT" -U "$DB_USER" -d "$DB_NAME" "$@"
    ;;
  reset)
    echo "Удаление кластера $PGDATA (только локальные данные разработки)."
    "$PG_BIN/pg_ctl" -D "$PGDATA" -m fast -w stop >/dev/null 2>&1 || true
    rm -rf "$PGDATA"
    ;;
  *)
    echo "Использование: $0 {start|stop|status|psql|reset}" >&2
    exit 2
    ;;
esac
