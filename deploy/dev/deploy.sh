#!/usr/bin/env bash
# Run on the API VM. The GHCR token is supplied on stdin, never in argv/files.
set -Eeuo pipefail
umask 077

root='/opt/capstone/backend-dev'
release=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
image=${1:?Pass a GHCR image digest}
registry_user=${2:?Pass the GitHub registry username}
if [[ ! "$image" =~ ^ghcr\.io/[a-z0-9._/-]+@sha256:[a-f0-9]{64}$ ]]; then
  echo 'Expected an immutable GHCR image digest.' >&2
  exit 1
fi
relative_release=${release#"$root/"}
if [[ "$release" != "$root/"* || ! "$relative_release" =~ ^releases/[0-9]+-[0-9]+$ ]]; then
  echo 'Deploy script must run from a numbered release under /opt/capstone/backend-dev/releases.' >&2
  exit 1
fi
cd "$root"
exec 9> .deploy.lock
flock -w 300 9
test -s api.env || { echo 'Create the private api.env on the VM first.' >&2; exit 1; }
chmod 600 api.env
docker network inspect capstone_default >/dev/null

# A temporary Docker config avoids saving registry credentials on the VM.
DOCKER_CONFIG=$(mktemp -d "$root/.registry.XXXXXX")
[[ "$DOCKER_CONFIG" == "$root/.registry."* ]] || exit 1
export DOCKER_CONFIG
trap 'rm -rf -- "$DOCKER_CONFIG"' EXIT
docker login ghcr.io --username "$registry_user" --password-stdin >/dev/null
export API_IMAGE="$image" API_ENV_FILE="$root/api.env"

previous_release=''
previous_image=''
if [[ -f .active-release ]]; then
  mapfile -t previous < .active-release
  previous_release=${previous[0]:-}
  previous_image=${previous[1]:-}
  if [[ ! "$previous_release" =~ ^releases/[0-9]+-[0-9]+$ || ! "$previous_image" =~ ^ghcr\.io/[a-z0-9._/-]+@sha256:[a-f0-9]{64}$ || ! -f "$root/$previous_release/compose.yml" ]]; then
    echo 'Previous release state is invalid; no container was changed.' >&2
    exit 1
  fi
elif docker container inspect capstone-backend-dev >/dev/null 2>&1; then
  echo 'An unmanaged capstone-backend-dev container already exists; inspect it before adopting this deployment.' >&2
  exit 1
fi

compose() { docker compose -f "$release/compose.yml" "$@"; }
# Validate quietly: expanded config contains secrets and must not enter CI logs.
compose config --quiet
compose pull api
if compose up --detach --no-deps --wait --wait-timeout 120 api; then
  printf '%s\n%s\n' "$relative_release" "$image" > .active-release.next
  mv -f .active-release.next .active-release
  echo "Backend Dev is ready: $image"
else
  echo 'New API did not become ready. Restoring the previous release when available.' >&2
  if [[ -n "$previous_release" ]]; then
    export API_IMAGE="$previous_image"
    if ! docker compose -f "$root/$previous_release/compose.yml" up --detach --no-deps --wait --wait-timeout 120 api; then
      echo 'Rollback also failed. Check SQL connectivity, api.env and container logs on the VM.' >&2
    fi
  else
    compose stop api || true
    echo 'First deployment failed; the new API has been stopped. Persistent volumes remain.' >&2
  fi
  exit 1
fi
