#!/usr/bin/env bash
# Восстановление DrawingFlow из копии, созданной scripts/backup.sh.
# Использование: scripts/restore.sh <каталог_копии> --yes
# ВНИМАНИЕ: текущая база и каталог хранилища будут заменены содержимым копии. Остановите сервер перед восстановлением.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PG_BIN="${PG_BIN:-/Library/PostgreSQL/16/bin}"
export PGHOST="${DF_PGHOST:-localhost}" PGPORT="${DF_PGPORT:-5433}" PGUSER="${DF_PGUSER:-drawingflow}" PGPASSWORD="${DF_PGPASSWORD:-drawingflow}"
DB="${DF_PGDATABASE:-drawingflow}"
STORAGE="${DF_STORAGE:-$REPO_ROOT/.local/storage}"
SRC="${1:-}"

if [[ -z "$SRC" || ! -f "$SRC/drawingflow.dump" ]]; then
  echo "Использование: $0 <каталог_копии> --yes (в каталоге должен быть drawingflow.dump)" >&2
  exit 2
fi
if [[ "${2:-}" != "--yes" ]]; then
  echo "Восстановление заменит базу $DB и каталог $STORAGE. Для подтверждения добавьте --yes." >&2
  exit 2
fi

echo "Проверка контрольной суммы дампа..."
expected="$(grep '^dump_sha256=' "$SRC/manifest.txt" | cut -d= -f2 || true)"
actual="$(shasum -a 256 "$SRC/drawingflow.dump" | cut -d' ' -f1)"
if [[ -n "$expected" && "$expected" != "$actual" ]]; then
  echo "Контрольная сумма дампа не совпадает с манифестом — копия повреждена." >&2
  exit 1
fi

echo "Пересоздание базы $DB..."
"$PG_BIN/psql" -d postgres -qtA -c "select pg_terminate_backend(pid) from pg_stat_activity where datname = '$DB' and pid <> pg_backend_pid()" >/dev/null
"$PG_BIN/dropdb" --if-exists "$DB"
"$PG_BIN/createdb" "$DB"
"$PG_BIN/pg_restore" -d "$DB" --no-owner --no-privileges "$SRC/drawingflow.dump"

if [[ -f "$SRC/storage.tar.gz" ]]; then
  echo "Восстановление хранилища в $STORAGE..."
  rm -rf "$STORAGE"
  mkdir -p "$(dirname "$STORAGE")"
  tar -xzf "$SRC/storage.tar.gz" -C "$(dirname "$STORAGE")"
fi

echo "Готово. Проверьте соответствие версий и файлов: dotnet run --project src/DrawingFlow.Server -- --verify-storage"
