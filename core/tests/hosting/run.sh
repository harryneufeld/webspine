#!/bin/sh
set -eu
cd "$(dirname "$0")"
# Unique project: no published ports or production mounts.
COMPOSE_PROJECT_NAME="webspine-hosting-$(date +%s)-$$"
export COMPOSE_PROJECT_NAME
cleanup() {
    result=$?
    if [ "$result" -ne 0 ]; then docker compose logs --no-color; fi
    docker compose down --volumes --remove-orphans
    exit "$result"
}
trap cleanup EXIT
docker compose build
docker compose run --rm maintenance prepare
docker compose up -d apache fpm nginx backend cloudpanel cloudpanel-broken
docker compose exec apache apache2ctl -t
for server in nginx backend cloudpanel cloudpanel-broken; do
    docker compose exec "$server" nginx -t -c "/work/$server.conf"
done
docker compose run --rm check initial
for server in apache nginx cloudpanel; do
    docker compose run --rm maintenance reset-contact
    docker compose run --rm form-check "$server"
done
docker compose run --rm maintenance deny-storage
docker compose run --rm check denied
docker compose run --rm maintenance allow-storage
docker compose run --rm check initial
# Stop/drain every PHP worker before backing up and activating; startup resets OPcache.
docker compose stop apache fpm
docker compose run --rm maintenance update
docker compose up -d apache fpm
# Docker may reassign a stopped container's address; refresh resolved upstreams.
docker compose exec nginx nginx -s reload -c /work/nginx.conf
docker compose exec backend nginx -s reload -c /work/backend.conf
docker compose run --rm check updated
for server in apache nginx cloudpanel; do
    docker compose run --rm maintenance reset-contact
    docker compose run --rm form-check "$server"
done
docker compose stop apache fpm
docker compose run --rm maintenance rollback
docker compose up -d apache fpm
docker compose exec nginx nginx -s reload -c /work/nginx.conf
docker compose exec backend nginx -s reload -c /work/backend.conf
docker compose run --rm check restored
for server in apache nginx cloudpanel; do
    docker compose run --rm maintenance reset-contact
    docker compose run --rm form-check "$server"
done
