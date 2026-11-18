# Backend Main deployment

The [Main workflow](../.github/workflows/deploy-main.yml) runs on pushes to `main` and supports a manual run on that branch. It tests the backend, publishes a Linux amd64 image to GHCR with a `main-<commit SHA>` tag, and sends its immutable digest to the production API VM through Tailscale and OpenSSH.

| Setting | Production value |
| --- | --- |
| GitHub Environment | `production` |
| VM directory | `/opt/capstone/backend-main` |
| Compose project and container | `capstone-backend-main` |
| API upstream | `192.168.1.206:5081` |
| SQL | `capstone-sqlserver,1433` on `capstone_default`, database `FA26SE103` |
| Runtime files on VM | `compose.yml`, `.env`, `api.env` |
| State volume | `capstone_backend_main_state` |
| Public API | `https://supermarket-api.kitsuracloud.com` |

## Prepare the GitHub Environment

In **Settings → Environments**, create `production` and select `main` as its deployment branch. Add the following environment secrets. The deployment job uses the repository's temporary `GITHUB_TOKEN` for GHCR.

| Secret | Value |
| --- | --- |
| `TS_OAUTH_CLIENT_ID` | Tailscale OAuth client ID allowed to issue `tag:ci` |
| `TS_OAUTH_SECRET` | Matching Tailscale OAuth secret |
| `PROD_SSH_PRIVATE_KEY` | Authorized OpenSSH deployment private key for the production VM |
| `PROD_SSH_KNOWN_HOSTS` | Verified SSH server host-key entry for the production VM |

The defaults are `PROD_SSH_HOST=100.105.191.20`, `PROD_SSH_USER=shiori`, and `PROD_SSH_PORT=22`. Set environment variables with those names if the address, account or port differs. The VM must accept OpenSSH connections from the runner's `tag:ci` under the Tailnet policy. The SSH account needs Docker access and write access to the Main deployment directory. See the [Dev workflow setup](DEPLOY_DEV.md) for the shared Tailscale arrangement.

For a dedicated Main key, generate an Ed25519 pair on a trusted machine under a new filename. Keep the passphrase empty for the unattended runner. Append the **public** key to `~/.ssh/authorized_keys` for the deployment user on the VM and save the **private** key directly in `PROD_SSH_PRIVATE_KEY`. Keep the `.ssh` directory at mode `700` and the authorized keys file at mode `600`.

On the VM, use an existing trusted session to obtain its public host key:

```bash
printf '100.105.191.20 '
cat /etc/ssh/ssh_host_ed25519_key.pub
```

Put the resulting line in `PROD_SSH_KNOWN_HOSTS`. Use the configured hostname, or `[host]:port` for a nondefault port. The runner uses strict host-key verification.

## Adopt the manually deployed Main stack

Keep the existing Main runtime files in `/opt/capstone/backend-main`. Leave `Bootstrap__Enabled=false` after provisioning the Admin. The script verifies the resolved Compose config targets the Main container, port `5081` and database `FA26SE103`. The app runtime secrets remain in the VM's private `api.env`.

Before the first automated run, check the image that is running and the image recorded in the Main `.env`:

```bash
cd /opt/capstone/backend-main
docker inspect --format '{{.Config.Image}} {{.State.Health.Status}}' capstone-backend-main
grep '^API_IMAGE=' .env
docker compose config --quiet
```

For the current manual setup both should show the same local `capstone-backend-main:<SHA>-r1` image. Keep that image on the VM for rollback.

## Run and verify

After the secrets and SSH key are in place, merge the workflow into `main`. The push starts **Deploy backend Main** automatically. To retry after configuration is fixed, open that workflow under **Actions → Run workflow** and select `main`.

On the VM the deployment uses the existing Main Compose project, configuration and volume. It pulls the new image first and checks that the non-root runtime user can read both appsettings files. It then updates the API, waits for container health and checks readiness via the host port. After those checks succeed it records the new digest in `.env`. A failed rollout restores the previous locally available image, including the manual image on the first rollout. Keep the previous image available for recovery.

```bash
curl -fsS https://supermarket-api.kitsuracloud.com/health/ready
curl -fsS https://supermarket-api-dev.kitsuracloud.com/health/ready
```

Both endpoints should return `READY`. The workflow changes the backend image. SQL migrations, VM configuration, and Nginx or DNS changes follow their own deployment steps.
