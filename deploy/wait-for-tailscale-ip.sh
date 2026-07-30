#!/bin/bash
# Used as nboard-server.service's ExecStartPre (see deploy/nboard-server.service).
#
# tailscaled's systemd Type=notify readiness signal fires once its own daemon/IPC socket is up, but
# that can happen before the WireGuard interface actually has its Tailscale IP configured - a boot-time
# race where nboard-server starts right after tailscaled per After=tailscaled.service, and Kestrel fails
# to bind with "Cannot assign requested address" because the IP doesn't exist on any interface yet
# (observed in production: SocketException(99) from SocketTransportOptions.CreateDefaultBoundListenSocket,
# systemd's Restart=on-failure recovered it a few seconds later, but the race isn't guaranteed to resolve
# that fast). Poll until the IP is actually present before letting ExecStart run.
set -euo pipefail

tailscale_ip="$1"
timeout_sec="${2:-30}"

waited=0
while ! ip -4 addr show tailscale0 2>/dev/null | grep -q "$tailscale_ip"; do
  if [ "$waited" -ge "$timeout_sec" ]; then
    echo "Timed out after ${timeout_sec}s waiting for $tailscale_ip on tailscale0" >&2
    exit 1
  fi
  sleep 1
  waited=$((waited + 1))
done

echo "$tailscale_ip is up on tailscale0 (waited ${waited}s)"
