# Deployment: EC2 match server + always-on static results viewer

This covers the production layout: the match server (this app) runs on an EC2 instance that stops
itself after being idle, a static, read-only results viewer lives somewhere that's always up (Oracle
Cloud Always Free, S3, GitHub Pages, Cloudflare Pages — any static host works), and a small Lambda lets
that static page start the EC2 instance back up on demand.

```
 [ your devices: PC + phone, enrolled in your Tailscale network ]
       |  (1) browse past results any time, EC2 or not
       v
 [ static host: history-export/ mirror ]  <-- synced after every match by HistorySyncCommand
       |  (2) run wake-and-connect.ps1 (or tap the site's Wake button) to start a new one
       v
 [ Lambda Function URL ]  --ec2:StartInstances-->  [ EC2 instance, stopped ]
       |
       v
 [ EC2 instance, running ]  <-- reachable directly at its Tailscale IP, from enrolled devices only
       |
       v  (idle timeout)
 [ EC2 instance stops itself ]  <-- Ec2SelfStopService, no Lambda involved for this direction
```

The live app is never reachable from the open internet. By default it's loopback-only (`127.0.0.1`,
local dev's setting); in production it's bound instead to the instance's Tailscale IP (`--bind-address`),
a private address that only devices you've explicitly enrolled in your own Tailscale network can ever
reach — no login screen needed, Tailscale's own device enrollment is the access control. The only two
pieces ever reachable from the open internet are the static results mirror (read-only, no secrets) and
the Lambda (can only Start/Describe the one configured instance).

## 1. Instance self-stop (already wired into the app)

The app calls `ec2:StopInstances` on itself once the match queue has been empty for
`AutoStopIdleMinutes` (Settings sidebar, default 15). It uses the instance's own IAM instance profile
credentials — no keys to manage.

1. Create an IAM role for the EC2 instance (or edit its existing one) and attach an inline policy from
   [`ec2-instance-self-stop-policy.json`](./ec2-instance-self-stop-policy.json), filling in your region,
   account ID, and instance ID (the policy can only be attached *after* the instance exists, since it
   needs the instance's own ARN).
2. Attach that role to the EC2 instance (as its IAM instance profile).
3. Make sure the instance is **stopped**, not **terminated**, when idle — stopping preserves the EBS
   volume (and therefore `data/`) across restarts; terminating destroys it.
4. In the app's sidebar, turn on **Auto-shutdown** and set the idle-minutes threshold.

If the app isn't running on an EC2 instance (e.g. local dev), this feature safely no-ops and logs
"not running on an EC2 instance" instead of erroring.

## 2. Static results viewer

Every completed match gets mirrored into `data/history-export/` automatically:
`index.html` (the viewer, from `Web/StaticExport/index.html`), `history-index.json` (a manifest of every
match), and `<matchId>/{stats.json, record.ggf}` per match. This folder is self-contained — sync it
wholesale to any static host and it works with zero backend.

1. Pick a static host and create a bucket/site (Oracle Object Storage static website, S3 + CloudFront,
   Cloudflare Pages, GitHub Pages, etc.).
2. Set **History sync command** in the app's sidebar to whatever pushes a local folder to that host.
   The token `{dir}` is replaced with `data/history-export`'s absolute path. Examples:
   - Oracle Object Storage (OCI CLI): `oci os object bulk-upload -bn your-bucket --src-dir {dir} --overwrite-if-changed`
   - Any S3-compatible target via rclone (works for Oracle, S3, R2, etc. with one remote config):
     `rclone sync {dir} remote:your-bucket`
   - Plain S3: `aws s3 sync {dir} s3://your-bucket --delete`
3. The command runs after every completed match. Failures are logged but never fail the match itself.

## 3. Wake-on-demand Lambda

Lets the static page start the stopped EC2 instance without embedding AWS credentials anywhere public.

1. In AWS Lambda, create a function (Node.js 20.x runtime, "Author from scratch"). Paste in
   [`ec2-start-lambda/index.mjs`](./ec2-start-lambda/index.mjs) — the Node 18+/20.x managed runtime
   bundles the AWS SDK v3 (`@aws-sdk/client-ec2`) already, so no zip/layer upload is needed.
2. Set environment variables on the function:
   - `INSTANCE_ID` = your instance ID (e.g. `i-0123456789abcdef0`)
   - `SHARED_TOKEN` (optional) = any string, if you want a basic deterrent against random callers
3. Attach an execution role with the inline policy from
   [`ec2-start-lambda/iam-policy.json`](./ec2-start-lambda/iam-policy.json) (fill in region/account/instance ID).
4. Enable a **Function URL** for the function (Auth type: `NONE` — the IAM policy above is what actually
   limits blast radius, not this). Copy the resulting URL.
5. On the static host, edit `history-export/config.js` (seeded once from `config.example.js`, then never
   overwritten again by future syncs — your edits are safe) and set `functionUrl` to the Function URL
   from step 4, plus `token` if you set `SHARED_TOKEN`.
6. Re-run (or wait for) the sync command so the edited `config.js` reaches the static host.

Once configured, the results page shows a "Wake match server" button with a live status line. See the
next section for how a device actually reaches the app once it's up.

## 4. Restricting access to specific devices with Tailscale (recommended)

The goal: only your own PC and phone can ever reach the live app, from anywhere, on any network,
without an SSH key, a login screen, or a public HTTPS endpoint to maintain. [Tailscale](https://tailscale.com)
(a mesh VPN built on WireGuard) does this by giving every enrolled device a stable private IP
(`100.x.x.x`) and only routing traffic between devices you've explicitly signed into the same account —
that enrollment step *is* the access control, so no custom login page is needed.

One-time setup:

1. On the EC2 instance: `curl -fsSL https://tailscale.com/install.sh | sh` then `sudo tailscale up`.
   This prints a URL — open it once in any browser to link the instance to your Tailscale account.
2. Run `tailscale ip -4` on the instance to get its stable Tailscale IP. It stays the same across
   stop/start (Tailscale's device identity lives in `/var/lib/tailscale/` on the instance's own root
   volume, which persists as long as you stop rather than terminate — same rule as `data/`).
3. Start the app with `--bind-address <that Tailscale IP>` instead of the default `127.0.0.1`, so it's
   reachable over the Tailscale network specifically (not the public internet — the instance's public
   IP still has nothing listening on the app's port).
4. Install the Tailscale app on your PC and your phone, and sign in with the same account. In the
   [Tailscale admin console](https://login.tailscale.com/admin/machines), each device shows up as a
   machine you can individually revoke later if needed.
5. Copy [`wake-and-connect.config.example.json`](./wake-and-connect.config.example.json) to
   `wake-and-connect.config.json`, and fill in `functionUrl` and `tailscaleIp` (from step 2).

From then on, [`wake-and-connect.ps1`](./wake-and-connect.ps1) wakes the instance if needed and opens
`http://<tailscale-ip>:5000` in your browser — works the same from your PC or your phone (the Tailscale
app on the phone just needs to be connected; open the URL manually there since the script is PC-only).
Security-group-wise, the app's port never needs to be open to `0.0.0.0/0` at all — only Tailscale's own
NAT-traversal port (UDP 41641) benefits from being open, and even that is optional since Tailscale falls
back to relaying when it can't punch through.

## 5. Alternative: AWS Systems Manager Session Manager (no third-party service, PC only)

If you'd rather not depend on Tailscale, plain port forwarding via AWS's own Session Manager also avoids
SSH keys and works despite the instance's public IP changing on restart — it targets the instance by its
fixed instance ID. It doesn't extend as naturally to a phone (it needs the AWS CLI + Session Manager
plugin), so it's better suited to single-device (PC-only) access than the "specific phone + PC" case above.

1. Attach the AWS managed policy `AmazonSSMManagedInstanceCore` to the instance's IAM role (in addition
   to the self-stop policy from step 1). Most current Amazon Linux / Ubuntu AMIs already run the SSM Agent.
2. Install the [AWS CLI v2](https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html)
   and the [Session Manager plugin](https://docs.aws.amazon.com/systems-manager/latest/userguide/session-manager-working-with-install-plugin.html)
   locally, and run `aws configure`.
3. Grant that identity the policy in [`ssm-port-forwarding-user-policy.json`](./ssm-port-forwarding-user-policy.json)
   (fill in region/account/instance ID).
4. Connect with:
   ```
   aws ssm start-session --target <instance-id> --document-name AWS-StartPortForwardingSession --parameters portNumber=5000,localPortNumber=5000
   ```
   then open `http://localhost:5000`. <kbd>Ctrl+C</kbd> closes the tunnel.
