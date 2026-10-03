#!/usr/bin/env bash
# Exercise rollout/rollback with fake Docker/locking; no daemon, server or secrets.
set -Eeuo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
mkdir -p "$repo/.local/deploy-tests"
scratch=$(mktemp -d "$repo/.local/deploy-tests/run.XXXXXX")
scratch=$(cd "$scratch" && pwd -P)
[[ "$scratch" == "$repo/.local/deploy-tests/run."* ]] || exit 1
trap 'rm -rf -- "$scratch"' EXIT
mkdir "$scratch/bin"
export PATH="$scratch/bin:$PATH"
cat > "$scratch/bin/docker" <<'MOCK'
#!/usr/bin/env bash
set -eu
echo "$*" >> "$MOCK_CALLS"
case "$1" in
  login) cat >/dev/null; touch "$DOCKER_CONFIG/config.json"; exit 0 ;;
  network) exit 0 ;;
  container) exit 1 ;;
  compose)
    file=$3
    operation=$4
    case "$operation" in
      config) [[ "$5" == --quiet ]]; exit 0 ;;
      pull) exit "${MOCK_PULL_FAIL:-0}" ;;
      up)
        printf '%s %s\n' "$file" "$API_IMAGE" >> "$MOCK_UPS"
        if [[ "${MOCK_NEW_FAIL:-0}" == 1 && "$API_IMAGE" == "$NEXT_IMAGE" ]]; then exit 1; fi
        if [[ "${MOCK_ROLLBACK_FAIL:-0}" == 1 && "$API_IMAGE" != "$NEXT_IMAGE" ]]; then exit 1; fi
        exit 0 ;;
      stop) exit 0 ;;
    esac ;;
esac
echo 'Unexpected fake Docker operation' >&2
exit 98
MOCK
printf '#!/usr/bin/env bash\nexit 0\n' > "$scratch/bin/flock"
chmod +x "$scratch/bin/docker" "$scratch/bin/flock"
NEXT_IMAGE="ghcr.io/example/backend@sha256:$(printf 'a%.0s' {1..64})"
OLD_IMAGE="ghcr.io/example/backend@sha256:$(printf 'b%.0s' {1..64})"
export NEXT_IMAGE

prepare() {
  local name=$1 previous=$2
  root="$scratch/$name"
  mkdir -p "$root/releases/1-1" "$root/releases/2-1"
  # Replace only the deployment directory in a private fixture copy.
  sed "s|/opt/capstone/backend-dev|$root|g" "$repo/deploy/dev/deploy.sh" > "$root/releases/2-1/deploy.sh"
  cp "$repo/deploy/dev/compose.yml" "$root/releases/1-1/compose.yml"
  cp "$repo/deploy/dev/compose.yml" "$root/releases/2-1/compose.yml"
  printf 'Jwt__Key=dummy\n' > "$root/api.env"
  if [[ "$previous" == yes ]]; then printf '%s\n%s\n' 'releases/1-1' "$OLD_IMAGE" > "$root/.active-release"; fi
  export MOCK_CALLS="$root/calls" MOCK_UPS="$root/ups"
  unset MOCK_NEW_FAIL MOCK_PULL_FAIL MOCK_ROLLBACK_FAIL
}
run_deploy() { printf 'dummy-token' | bash "$root/releases/2-1/deploy.sh" "$NEXT_IMAGE" example > "$root/output" 2>&1; }
clean_registry() { [[ -z $(find "$root" -maxdepth 1 -name '.registry.*' -print) ]]; }
previous_state() { [[ $(cat "$root/.active-release") == "$(printf '%s\n%s' 'releases/1-1' "$OLD_IMAGE")" ]]; }

prepare success no
run_deploy
[[ $(cat "$root/.active-release") == "$(printf '%s\n%s' 'releases/2-1' "$NEXT_IMAGE")" ]]
[[ $(wc -l < "$MOCK_UPS") == 1 ]]
clean_registry
echo 'PASS: first rollout records ready digest and removes temporary registry credentials'

prepare update yes
run_deploy
[[ $(sed -n '2p' "$root/.active-release") == "$NEXT_IMAGE" ]]
clean_registry
echo 'PASS: successful update advances release state'

prepare rollback yes
export MOCK_NEW_FAIL=1
if run_deploy; then echo 'Expected failed rollout' >&2; exit 1; fi
previous_state
[[ $(wc -l < "$MOCK_UPS") == 2 ]]
[[ $(tail -n 1 "$MOCK_UPS") == "$root/releases/1-1/compose.yml $OLD_IMAGE" ]]
clean_registry
echo 'PASS: failed readiness restores previous image and Compose without advancing state'

prepare pull-failure yes
export MOCK_PULL_FAIL=1
if run_deploy; then echo 'Expected failed pull' >&2; exit 1; fi
previous_state
[[ ! -f "$MOCK_UPS" ]]
clean_registry
echo 'PASS: failed pull leaves running container and release state untouched'

prepare first-failure no
export MOCK_NEW_FAIL=1
if run_deploy; then echo 'Expected failed first rollout' >&2; exit 1; fi
[[ ! -f "$root/.active-release" ]]
[[ $(tail -n 1 "$MOCK_CALLS") == *'stop api' ]]
clean_registry
echo 'PASS: failed first rollout stops only API and preserves volumes'

prepare failed-rollback yes
export MOCK_NEW_FAIL=1 MOCK_ROLLBACK_FAIL=1
if run_deploy; then echo 'Expected failed rollback' >&2; exit 1; fi
previous_state
[[ $(wc -l < "$MOCK_UPS") == 2 ]]
[[ $(cat "$root/output") == *'Rollback also failed.'* ]]
clean_registry
echo 'PASS: failed rollback reports failure and does not claim new success'
