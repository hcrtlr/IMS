#!/usr/bin/env bash
# Complete from-scratch verification.
#
# Destroys the database entirely, wipes all build output, rebuilds from source,
# applies migrations to an empty database, runs the unit tests, starts the API,
# and runs all six phase verification suites in order.
set -e

PGBIN="C:/Program Files/PostgreSQL/15/bin"
REPO="C:/Users/hacer/Desktop/IMS"
HERE="$(cd "$(dirname "$0")" && pwd)"
CONN="Host=127.0.0.1;Port=5433;Database=ims;Username=imsdev;Password=devpass"
export PGPASSWORD=devpass

line() { printf '\n=== %s ===\n' "$1"; }

line "1. Stopping any running API"
powershell.exe -NoProfile -Command \
  "Get-Process -Name 'IMS.Api' -ErrorAction SilentlyContinue | ForEach-Object { \$_.Kill() }" >/dev/null 2>&1 || true
sleep 2

line "2. Destroying the database"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d postgres -w -q \
  -c "DROP DATABASE IF EXISTS ims WITH (FORCE);"
echo "dropped"

line "3. Wiping all build output"
cd "$REPO"
rm -rf src/*/bin src/*/obj tests/*/bin tests/*/obj 2>/dev/null || true
echo "bin/obj removed"

line "4. Restoring and building from clean"
dotnet restore > "$HERE/full_restore.log" 2>&1
dotnet build -c Debug --nologo > "$HERE/full_build.log" 2>&1
grep -cE "warning (CS|EF)" "$HERE/full_build.log" | xargs -I{} echo "compiler warnings: {}"
echo "build OK"

line "5. Running unit tests"
dotnet test tests/IMS.Tests --no-build --nologo -v q 2>&1 | grep -iE "Basar|Passed|Failed|Total" | head -3

line "6. Creating an empty database and applying migrations"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d postgres -w -q -c "CREATE DATABASE ims;"
IMS_MIGRATIONS_CONNECTION="$CONN" dotnet ef database update \
  --project src/IMS.Infrastructure --startup-project src/IMS.Infrastructure --no-build 2>&1 \
  | grep -E "Applying|Done" || true

line "7. Schema created"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A \
  -c "SELECT count(*) || ' tables' FROM information_schema.tables WHERE table_schema='public';"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A \
  -c "SELECT count(*) || ' indexes' FROM pg_indexes WHERE schemaname='public';"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A \
  -c "SELECT count(*) || ' check constraints' FROM pg_constraint WHERE contype='c';"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A \
  -c "SELECT count(*) || ' foreign keys' FROM pg_constraint WHERE contype='f';"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A \
  -c "SELECT count(*) || ' triggers' FROM pg_trigger WHERE NOT tgisinternal;"

line "8. Starting the API (migrates + seeds on boot)"
cd "$REPO"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5080" \
  nohup dotnet run --project src/IMS.Api --no-launch-profile --no-build \
  > "$HERE/api.log" 2>&1 &

for i in $(seq 1 90); do
  code=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5080/health 2>/dev/null || true)
  [ "$code" = "200" ] && { echo "API healthy after ${i}s"; break; }
  sleep 1
done
[ "$code" = "200" ] || { echo "API FAILED TO START"; tail -40 "$HERE/api.log"; exit 1; }

line "9. Seeded reference data"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A \
  -c "SELECT 'inventory statuses: ' || count(*) FROM inventory_statuses;"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A \
  -c "SELECT 'units of measure: ' || count(*) FROM units_of_measure;"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A \
  -c "SELECT 'users: ' || count(*) FROM users;"

line "10. Running all six phase verification suites"
cd "$HERE"
total_pass=0
total_all=0
failed_suites=0

for f in 1 2 3 4 5 6; do
  out=$(python "verify_faz$f.py" 2>&1) || failed_suites=$((failed_suites+1))
  result=$(echo "$out" | grep "RESULT" || echo "FAZ $f RESULT: SUITE ERROR")
  echo "$result"
  echo "$out" | grep -E "^\[FAIL\]" | head -5 || true
  p=$(echo "$result" | grep -oE "[0-9]+/[0-9]+" | cut -d/ -f1)
  a=$(echo "$result" | grep -oE "[0-9]+/[0-9]+" | cut -d/ -f2)
  total_pass=$((total_pass + ${p:-0}))
  total_all=$((total_all + ${a:-0}))
done

line "11. Final integrity check"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A -c \
  "SELECT 'negative balances: ' || count(*) FROM inventory_balances
   WHERE \"OnHandQuantity\" < 0 OR \"AllocatedQuantity\" < 0
      OR \"HoldQuantity\" < 0 OR \"AvailableQuantity\" < 0;"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A -c \
  "SELECT 'balances violating the 5.1 formula: ' || count(*) FROM inventory_balances
   WHERE \"AvailableQuantity\" <> \"OnHandQuantity\" - \"AllocatedQuantity\" - \"HoldQuantity\";"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A -c \
  "SELECT 'ledger rows: ' || count(*) FROM inventory_transactions;"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A -c \
  "SELECT 'ledger rows with no actor: ' || count(*) FROM inventory_transactions
   WHERE \"PerformedBy\" IS NULL;"
"$PGBIN/psql.exe" -h 127.0.0.1 -p 5433 -U imsdev -d ims -w -t -A -c \
  "SELECT 'orders shipped: ' || count(*) FROM orders WHERE \"Status\" = 9;"

printf '\n========================================================\n'
printf 'FROM-SCRATCH RUN COMPLETE\n'
printf 'Verification checks: %s/%s passed across 6 suites\n' "$total_pass" "$total_all"
printf 'Suites with failures: %s\n' "$failed_suites"
printf '========================================================\n'

[ "$failed_suites" -eq 0 ] || exit 1
