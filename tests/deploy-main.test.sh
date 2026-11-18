#!/usr/bin/env bash
set -Eeuo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
mkdir -p "$repo/.local/deploy-tests"
scratch=$(mktemp -d "$repo/.local/deploy-tests/main.XXXXXX")
scratch=$(cd "$scratch" && pwd -P)
[[ "$scratch" == "$repo/.local/deploy-tests/main."* ]] || exit 1
trap 'rm -rf -- "$scratch"' EXIT
mkdir "$scratch/bin"
export PATH="$scratch/bin:$PATH"

cat > "$scratch/bin/docker" <<'MOCK'
#!/usr/bin/env bash
set -eu
printf '%s\n' "$*" >> "$MOCK_CALLS"
case "$1" in
  image) printf '%s\n' sha256:previous ;;
  container) printf '%s\n' "sha256:${MOCK_RUNNING_ID:-previous}" ;;
  login) cat >/dev/null; touch "$DOCKER_CONFIG/config.json" ;;
  run) exit "${MOCK_IMAGE_FAIL:-0}" ;;
  compose)
    [[ "$2 $3 $4 $6" == '--project-name capstone-backend-main --env-file -f' ]]
    case "$8" in
      config)
        [[ "${9} ${10}" == '--format json' ]]
        cat <<JSON
{"services":{"api":{"container_name":"capstone-backend-main","ports":[{"host_ip":"192.168.1.206","published":"5081","target":8080}],"environment":{"ASPNETCORE_ENVIRONMENT":"Production","Bootstrap__Enabled":"false","ConnectionStrings__SqlServer":"Server=capstone-sqlserver,1433;Database=${MOCK_DB:-FA26SE103};Password=test-secret"}}}}
JSON
        ;;
      pull) exit "${MOCK_PULL_FAIL:-0}" ;;
      up)
        printf '%s\n' "$API_IMAGE" >> "$MOCK_UPS"
        if [[ "$API_IMAGE" == "$NEXT_IMAGE" ]]; then exit "${MOCK_NEW_FAIL:-0}"; fi
        exit "${MOCK_ROLLBACK_FAIL:-0}"
        ;;
      *) exit 98 ;;
    esac
    ;;
  *) exit 99 ;;
esac
MOCK
cat > "$scratch/bin/curl" <<'MOCK'
#!/usr/bin/env bash
if [[ "$API_IMAGE" == "$NEXT_IMAGE" ]]; then exit "${MOCK_READY_FAIL:-0}"; fi
exit 0
MOCK
printf '#!/usr/bin/env bash\nexit 0\n' > "$scratch/bin/flock"
chmod +x "$scratch/bin/"*

export NEXT_IMAGE="ghcr.io/gfa26se39-fa26se103/back-end@sha256:$(printf 'a%.0s' {1..64})"
manual_image="capstone-backend-main:$(printf 'b%.0s' {1..40})-r1"
previous_digest="ghcr.io/gfa26se39-fa26se103/back-end@sha256:$(printf 'c%.0s' {1..64})"

prepare() {
  root="$scratch/$1"
  previous=$2
  mkdir "$root"
  sed "s|/opt/capstone/backend-main|$root|g" "$repo/deploy/main/deploy.sh" > "$root/deploy.sh"
  printf 'services: {}\n' > "$root/compose.yml"
  printf 'API_IMAGE=%s\nAPI_BIND_IP=192.168.1.206\nSQL_NETWORK=capstone_default\n' "$previous" > "$root/.env"
  cp "$root/.env" "$root/original.env"
  printf 'Jwt__Key=test-secret\n' > "$root/api.env"
  export MOCK_CALLS="$root/calls" MOCK_UPS="$root/ups"
  unset MOCK_RUNNING_ID MOCK_DB MOCK_PULL_FAIL MOCK_IMAGE_FAIL MOCK_NEW_FAIL MOCK_READY_FAIL MOCK_ROLLBACK_FAIL
}

deploy() {
  printf 'fake-registry-token' | bash "$root/deploy.sh" "$NEXT_IMAGE" example > "$root/output" 2>&1
}

expect_failure() {
  if deploy; then
    echo 'Expected deploy to fail' >&2
    exit 1
  fi
  cmp "$root/.env" "$root/original.env"
}

check_cleanup() {
  [[ -z $(find "$root" -maxdepth 1 \( -name '.registry.*' -o -name '.env.next.*' \) -print) ]]
  ! grep -Eq 'test-secret|fake-registry-token' "$root/output"
}

prepare adopt "$manual_image"
deploy
[[ $(sed -n 's/^API_IMAGE=//p' "$root/.env") == "$NEXT_IMAGE" ]]
[[ $(cat "$MOCK_UPS") == "$NEXT_IMAGE" ]]
grep -qx 'API_BIND_IP=192.168.1.206' "$root/.env"
grep -qx 'SQL_NETWORK=capstone_default' "$root/.env"
check_cleanup
echo 'PASS: adopts the existing manual Main deployment and updates only the image'

prepare rollout-rollback "$manual_image"
export MOCK_NEW_FAIL=1
expect_failure
[[ $(cat "$MOCK_UPS") == "$(printf '%s\n%s' "$NEXT_IMAGE" "$previous")" ]]
check_cleanup
echo 'PASS: restores the manual image when the new container is unhealthy'

prepare public-binding-rollback "$previous_digest"
export MOCK_READY_FAIL=1
expect_failure
[[ $(cat "$MOCK_UPS") == "$(printf '%s\n%s' "$NEXT_IMAGE" "$previous")" ]]
check_cleanup
echo 'PASS: restores the prior digest when the Main host endpoint fails'

prepare bad-rollback "$previous_digest"
export MOCK_NEW_FAIL=1 MOCK_ROLLBACK_FAIL=1
expect_failure
grep -q 'Rollback also failed' "$root/output"
check_cleanup
echo 'PASS: reports a failed rollback and keeps the previous image reference'

prepare pull-failure "$previous_digest"
export MOCK_PULL_FAIL=1
expect_failure
[[ ! -f "$MOCK_UPS" ]]
check_cleanup
echo 'PASS: failed image pull leaves the current container alone'

prepare unreadable-image "$previous_digest"
export MOCK_IMAGE_FAIL=1
expect_failure
[[ ! -f "$MOCK_UPS" ]]
check_cleanup
echo 'PASS: protects the running API from an image with inaccessible appsettings'

prepare wrong-database "$manual_image"
export MOCK_DB=FA26SE103_Dev
expect_failure
[[ ! -f "$MOCK_UPS" ]]
! grep -q '^login ' "$MOCK_CALLS"
check_cleanup
echo 'PASS: refuses to roll out a config pointing at the Dev database'

prepare image-drift "$manual_image"
export MOCK_RUNNING_ID=unexpected
expect_failure
[[ ! -f "$MOCK_UPS" ]]
! grep -q '^login ' "$MOCK_CALLS"
check_cleanup
echo 'PASS: refuses rollout if the active image differs from the rollback target'
