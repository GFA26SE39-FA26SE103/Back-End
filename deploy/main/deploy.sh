#!/usr/bin/env bash
# The GitHub Packages token is supplied on stdin.
set -Eeuo pipefail
umask 077

root='/opt/capstone/backend-main'
container='capstone-backend-main'
# Like Dev, CI copies deploy.sh and compose.yml into a numbered release directory.
release=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
image=${1:?Pass a GHCR image digest}
registry_user=${2:?Pass a GitHub username}
registry_image='^ghcr\.io/gfa26se39-fa26se103/back-end@sha256:[a-f0-9]{64}$'
if [[ ! "$image" =~ $registry_image || ! "$registry_user" =~ ^[a-zA-Z0-9_-]+$ ]]; then
  echo 'Expected an immutable image digest for the backend repository and a GitHub username.' >&2
  exit 1
fi

relative_release=${release#"$root/"}
if [[ "$release" != "$root/"* || ! "$relative_release" =~ ^releases/[0-9]+-[0-9]+$ ]]; then
  echo 'Deploy script must run from a numbered release under /opt/capstone/backend-main/releases.' >&2
  exit 1
fi
cd "$root"
exec 9>.deploy.lock
flock -w 300 9
if [[ ! -s "$release/compose.yml" || ! -s .env || ! -s api.env ]]; then
  echo 'The Main deployment requires the release compose.yml plus .env and api.env on the VM.' >&2
  exit 1
fi
export API_ENV_FILE="$root/api.env"
# $root/compose.yml is the last configuration that became ready; it is the rollback target.
next_compose="$release/compose.yml"
previous_compose="$root/compose.yml"
[[ -s "$previous_compose" ]] || previous_compose="$next_compose"
compose_file="$next_compose"

mapfile -t image_entries < <(sed -n 's/^API_IMAGE=//p' .env)
manual_image='^capstone-backend-main:[a-f0-9]{40}(-r[0-9]+)?$'
if [[ "${#image_entries[@]}" != 1 || ( ! "${image_entries[0]}" =~ $registry_image && ! "${image_entries[0]}" =~ $manual_image ) ]]; then
  echo 'Set API_IMAGE to the current Main release in the server .env before deploying.' >&2
  exit 1
fi
previous_image=${image_entries[0]}
previous_id=$(docker image inspect --format '{{.Id}}' "$previous_image")
if running_id=$(docker container inspect --format '{{.Image}}' "$container" 2>/dev/null); then
  if [[ "$running_id" != "$previous_id" ]]; then
    echo 'The running Main image differs from API_IMAGE. Reconcile the server .env before deploying.' >&2
    exit 1
  fi
fi

compose() {
  docker compose --project-name "$container" --env-file "$root/.env" -f "$compose_file" --project-directory "$root" "$@"
}

wait_for_api() {
  compose up --detach --no-deps --pull never --wait --wait-timeout 120 api &&
    curl -fsS --max-time 10 -H 'Host: supermarket-api.kitsuracloud.com' \
      http://192.168.1.206:5081/health/ready >/dev/null
}

export API_IMAGE="$image"
# Docker Compose renders runtime env values. Inspect them via a private pipe.
compose config --format json | python3 -c '
import json
import sys

cfg = json.load(sys.stdin)
api = cfg["services"]["api"]
env = api.get("environment", {})
sql_target = "Server=capstone-sqlserver,1433;Database=FA26SE103;"
if (
    api.get("container_name") != "capstone-backend-main"
    or not any(
        port.get("host_ip") == "192.168.1.206"
        and str(port.get("published")) == "5081"
        and port.get("target") == 8080
        for port in api.get("ports", [])
    )
    or env.get("ASPNETCORE_ENVIRONMENT") != "Production"
    or env.get("Bootstrap__Enabled", "false").lower() != "false"
    or not env.get("ConnectionStrings__SqlServer", "").startswith(sql_target)
):
    sys.exit("Expected the Main container, port 5081, the FA26SE103 database and disabled bootstrap.")
'

registry_dir=$(mktemp -d "$root/.registry.XXXXXX")
export DOCKER_CONFIG="$registry_dir"
pending_env=''
pending_compose=''
rollback=0

finish() {
  local result=$?
  trap - EXIT
  if (( rollback )); then
    echo "Restoring previous Main image and configuration: $previous_image" >&2
    export API_IMAGE="$previous_image"
    compose_file="$previous_compose"
    if wait_for_api; then
      echo 'Previous Main image is ready.' >&2
    else
      echo 'Rollback also failed. Inspect the Main container on the VM.' >&2
    fi
  fi
  if [[ -n "$pending_env" ]]; then rm -f -- "$pending_env"; fi
  if [[ -n "$pending_compose" ]]; then rm -f -- "$pending_compose"; fi
  rm -rf -- "$registry_dir"
  exit "$result"
}
trap finish EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

docker login ghcr.io --username "$registry_user" --password-stdin >/dev/null
compose pull api
docker run --rm --network none --entrypoint sh "$image" -c '
  test "$(id -u)" -ne 0 &&
  test -r /app/appsettings.json &&
  test -r /app/appsettings.Production.json
' || { echo 'The image must run as a non-root user and read its appsettings files.' >&2; exit 1; }
compose up --detach --wait --wait-timeout 45 redis

pending_env=$(mktemp "$root/.env.next.XXXXXX")
sed "s|^API_IMAGE=.*|API_IMAGE=$image|" .env > "$pending_env"
echo "Deploying $image. Current Main image: $previous_image"
rollback=1
if ! wait_for_api; then
  echo 'New Main image did not become ready.' >&2
  exit 1
fi
mv -f -- "$pending_env" .env
pending_compose=$(mktemp "$root/.compose.next.XXXXXX")
cp -- "$next_compose" "$pending_compose"
mv -f -- "$pending_compose" compose.yml
rollback=0
echo "Backend Main is ready: $image"
