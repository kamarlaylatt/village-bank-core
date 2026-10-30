#!/usr/bin/env bash
# Copies the settings the API needs from .env into .NET user-secrets, so `dotnet run` works.
# .NET does not read .env by itself. Run this again after changing a value in .env.
#
#   bash scripts/sync-secrets.sh
#
# Values are never printed.
set -euo pipefail

cd "$(dirname "$0")/.."

if [ ! -f .env ]; then
  echo ".env not found. Create it first:  cp .env.example .env" >&2
  exit 1
fi

project=src/VillageBank.Api

# Reads KEY from .env: everything after the first "=".
env_value() {
  grep -E "^$1=" .env | head -n 1 | cut -d= -f2- || true
}

set_secret() {
  local key="$1" value="$2"

  if [ -z "$value" ]; then
    echo "skipped  $key (empty in .env)"
    return
  fi

  # dotnet echoes the value it stored, so its output is discarded.
  dotnet user-secrets set "$key" "$value" --project "$project" > /dev/null
  echo "set      $key"
}

db_user=$(env_value POSTGRES_USER)
db_name=$(env_value POSTGRES_DB)
db_password=$(env_value POSTGRES_PASSWORD)

# The database runs in compose and is published on port 5433 (see docker-compose.yml).
if [ -n "$db_user" ] && [ -n "$db_name" ] && [ -n "$db_password" ]; then
  set_secret "ConnectionStrings:Default" \
    "Host=localhost;Port=5433;Database=$db_name;Username=$db_user;Password=$db_password"
else
  echo "skipped  ConnectionStrings:Default (POSTGRES_USER, POSTGRES_DB or POSTGRES_PASSWORD is empty in .env)"
fi

set_secret "Jwt:SigningKey"           "$(env_value Jwt__SigningKey)"
set_secret "Seed:SuperAdmin:Username" "$(env_value Seed__SuperAdmin__Username)"
set_secret "Seed:SuperAdmin:Password" "$(env_value Seed__SuperAdmin__Password)"
set_secret "Seed:Demo:Password"       "$(env_value Seed__Demo__Password)"

echo "Done. Restart dotnet run to pick up the changes."
