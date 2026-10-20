#!/bin/bash
# Administrator installs this as /usr/local/sbin/project1-auto-deploy, root:root 0755.
# CI can invoke only this fixed command, never sudo docker or an uploaded script.
set -Eeuo pipefail
if [[ $# != 3 || ! $1 =~ ^[0-9a-f]{40}$ || ! $2 =~ ^sha256:[0-9a-f]{64}$ || ! $3 =~ ^sha256:[0-9a-f]{64}$ ]]; then
    echo 'Usage: project1-auto-deploy COMMIT_SHA API_DIGEST FRONTEND_DIGEST' >&2
    exit 2
fi
[[ $(id -u) == 0 && $(hostname) == homelab-server ]] || exit 1
export PATH=/usr/sbin:/usr/bin:/sbin:/bin HOME=/root DOCKER_CONFIG=/root/.docker
unset DOCKER_HOST DOCKER_CONTEXT COMPOSE_FILE COMPOSE_PROFILES COMPOSE_PROJECT_NAME
umask 077
APP=/opt/project1-auto
COMMIT=$1
export API_IMAGE="ghcr.io/shunbin2310/project1-api@$2"
export FRONTEND_IMAGE="ghcr.io/shunbin2310/project1-frontend@$3"

for directory in / /opt "$APP"; do
    [[ -d $directory && ! -L $directory && $(stat -c %u "$directory") == 0 ]]
    mode=$(stat -c %a "$directory")
    (( (8#$mode & 8#022) == 0 ))
done
protected_file() {
    [[ -f $1 && ! -L $1 && $(stat -c '%u:%h' "$1") == 0:1 ]]
    local mode
    mode=$(stat -c %a "$1")
    (( (8#$mode & 8#022) == 0 ))
}
protected_file "$APP/compose.yml"
protected_file "$APP/.env"
[[ $(stat -c %a "$APP/.env") == 600 ]]
for file in deploy.lock release.env previous-release.env; do
    if [[ -e $APP/$file || -L $APP/$file ]]; then protected_file "$APP/$file"; fi
done
exec 9<>"$APP/deploy.lock"
flock -n 9 || { echo 'Another deployment is running.' >&2; exit 1; }
[[ ! -e $APP/needs-attention && ! -L $APP/needs-attention ]] || {
    echo 'Previous deployment needs administrator review; do not blindly retry.' >&2
    exit 1
}
# First replacement must be deliberately prepared by an administrator. Never run
# old and new API writers against Project1Db while a schema migration is starting.
[[ -z $(docker ps --filter label=com.docker.compose.project=project1-ubuntu-production --format '{{.Names}}') ]] || {
    echo 'Old Docker deployment is running. Complete the one-time cutover first.' >&2
    exit 1
}
compose() {
    docker compose --project-name project1-auto --project-directory "$APP" \
        --env-file "$APP/.env" -f "$APP/compose.yml" "$@"
}
compose config --quiet
# Failed pulls leave the existing app alone. Private GHCR login is configured on
# Ubuntu once; no registry/app password is passed in the SSH command or arguments.
compose pull

# A single recovery marker, not an automatic database rollback. Written before
# stopping the writer so cancellation/power loss also requires explicit review.
printf 'commit=%s\nAPI_IMAGE=%s\nFRONTEND_IMAGE=%s\n' \
    "$COMMIT" "$API_IMAGE" "$FRONTEND_IMAGE" > "$APP/needs-attention"
started=false
failed() {
    trap - ERR INT TERM
    if [[ $started == true ]]; then compose stop api >/dev/null 2>&1 || true; fi
    echo 'Deployment failed. Keep needs-attention and inspect; no automatic rollback or data restore.' >&2
    exit 1
}
trap failed ERR INT TERM
compose stop api
started=true
# Recreate both: Nginx resolves api at startup and must not keep its old IP.
compose up -d --force-recreate --no-build --pull never --wait --wait-timeout 420
curl --noproxy '*' --fail --silent --show-error --max-time 15 --output /dev/null http://127.0.0.1:8084/api/health
curl --noproxy '*' --fail --silent --show-error --max-time 15 --output /dev/null http://127.0.0.1/api/health
docker update --restart unless-stopped "$(compose ps -q api)" >/dev/null
if [[ -e $APP/release.env ]]; then
    install -m 0600 "$APP/release.env" "$APP/previous-release.env"
fi
temporary=$(mktemp "$APP/.release.XXXXXXXX")
printf 'COMMIT_SHA=%s\nAPI_IMAGE=%s\nFRONTEND_IMAGE=%s\n' \
    "$COMMIT" "$API_IMAGE" "$FRONTEND_IMAGE" > "$temporary"
mv -T -- "$temporary" "$APP/release.env"
rm -- "$APP/needs-attention"
trap - ERR INT TERM
printf 'Deployed %s. Both containers and original URL passed health checks.\n' "$COMMIT"
