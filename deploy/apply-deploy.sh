#!/bin/bash
# Applied on the EC2 instance via SSM RunCommand (see .github/workflows/deploy.yml and
# deploy/README.md section 6). Runs as root (SSM RunShellScript default).
#
# Waits for the match queue to be idle (no match currently running) before swapping in the new
# build and restarting the service, so an in-progress match is never killed mid-game. Pending
# (not-yet-started) queue entries are safe either way - QueueStore persists them to disk and
# QueueRunner resumes them automatically after restart.
set -euo pipefail

s3_path="$1"

app_user=ec2-user
app_home="/home/$app_user"
active_dir="$app_home/nboard-publish"
staging_dir="$app_home/nboard-publish-staging"
backup_dir="$app_home/nboard-publish-previous"
service_name=nboard-server
bind_address=100.107.146.54
port=5000
poll_interval_sec=15
max_wait_sec=170000 # stay under the 172800s (48h) SSM executionTimeout ceiling

echo "Fetching new build from $s3_path"
rm -rf "$staging_dir"
mkdir -p "$staging_dir"
aws s3 cp "$s3_path" /tmp/nboard-deploy.zip
unzip -q -o /tmp/nboard-deploy.zip -d "$staging_dir"
rm -f /tmp/nboard-deploy.zip
chown -R "$app_user:$app_user" "$staging_dir"

echo "Waiting for the match queue to go idle before restarting..."
waited=0
while true; do
  running=$(curl -s --max-time 5 "http://$bind_address:$port/api/queue" \
    | python3 -c 'import json, sys
try:
    print("busy" if json.load(sys.stdin).get("Running") else "idle")
except Exception:
    print("unreachable")' 2>/dev/null || echo "unreachable")

  if [ "$running" = "idle" ]; then
    echo "Queue is idle."
    break
  fi

  if [ "$running" = "unreachable" ]; then
    echo "Service unreachable (already stopped?) - proceeding with deploy."
    break
  fi

  if [ "$waited" -ge "$max_wait_sec" ]; then
    echo "Timed out after ${max_wait_sec}s waiting for the queue to go idle. Aborting - new build left staged at $staging_dir, active build untouched."
    exit 1
  fi

  sleep "$poll_interval_sec"
  waited=$((waited + poll_interval_sec))
done

echo "Swapping build and restarting $service_name..."
systemctl stop "$service_name"

rm -rf "$backup_dir"
if [ -d "$active_dir" ]; then
  mv "$active_dir" "$backup_dir"
fi
mv "$staging_dir" "$active_dir"

systemctl start "$service_name"
echo "Deploy complete."
