#!/usr/bin/env bash
# Резервная копия DrawingFlow: дамп базы PostgreSQL и архив каталога хранилища файлов.
# Использование: scripts/backup.sh [каталог_назначения]
# Параметры подключения берутся из переменных окружения (по умолчанию — dev-кластер scripts/dev-postgres.sh):
#   DF_PGHOST, DF_PGPORT, DF_PGUSER, DF_PGPASSWORD, DF_PGDATABASE, DF_STORAGE (каталог хранилища), PG_BIN.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PG_BIN="${PG_BIN:-/Library/PostgreSQL/16/bin}"
export PGHOST="${DF_PGHOST:-localhost}" PGPORT="${DF_PGPORT:-5433}" PGUSER="${DF_PGUSER:-drawingflow}" PGPASSWORD="${DF_PGPASSWORD:-drawingflow}"
DB="${DF_PGDATABASE:-drawingflow}"
STORAGE="${DF_STORAGE:-$REPO_ROOT/.local/storage}"
OUT="${1:-$REPO_ROOT/backups/$(date +%Y%m%d-%H%M%S)}"

mkdir -p "$OUT"
echo "Резервная копия базы $DB → $OUT/drawingflow.dump"
"$PG_BIN/pg_dump" -Fc -d "$DB" -f "$OUT/drawingflow.dump"

if [[ -d "$STORAGE" ]]; then
  echo "Архив хранилища $STORAGE → $OUT/storage.tar.gz"
  # Исключение временного каталога указывается до путей: так требуют и BSD tar (macOS), и GNU tar.
  tar --exclude="$(basename "$STORAGE")/.tmp" -czf "$OUT/storage.tar.gz" -C "$(dirname "$STORAGE")" "$(basename "$STORAGE")" 
else
  echo "Каталог хранилища $STORAGE отсутствует — архив не создан"
fi

{
  echo "created_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "database=$DB"
  echo "storage=$STORAGE"
  echo "versions=$("$PG_BIN/psql" -d "$DB" -qtA -c 'select count(*) from "DocumentVersions"')"
  echo "documents=$("$PG_BIN/psql" -d "$DB" -qtA -c 'select count(*) from "Documents"')"
  echo "dump_sha256=$(shasum -a 256 "$OUT/drawingflow.dump" | cut -d' ' -f1)"
} > "$OUT/manifest.txt"
echo "Готово. Манифест:"; cat "$OUT/manifest.txt"
