#!/usr/bin/env bash
# Local stack only (US-102). The postgres image runs this once, when its data volume is first created
# (/docker-entrypoint-initdb.d). It creates the `platform` database and the `platform_app` login role.
# platform_app is NOT a superuser and cannot bypass row-level security, so the RLS policies of US-110
# apply to it. Its password comes from PLATFORM_APP_PASSWORD in .env (written by scripts/local_stack.py).
# The entrypoint may source this file instead of running it, so it does not change shell options.

if [ -z "${PLATFORM_APP_PASSWORD:-}" ]; then
  echo "01-app-role.sh: PLATFORM_APP_PASSWORD is not set; run python scripts/local_stack.py up" >&2
  exit 1
fi

# The password is passed as a psql variable and quoted by psql (:'role_pw'), never pasted into SQL text.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  --set role_pw="$PLATFORM_APP_PASSWORD" <<'SQL'
CREATE ROLE platform_app WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS
  PASSWORD :'role_pw';
CREATE DATABASE platform;
REVOKE ALL ON DATABASE platform FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE platform TO platform_app;
SQL

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname platform <<'SQL'
GRANT USAGE ON SCHEMA public TO platform_app;
SQL
