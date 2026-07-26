// Copied to config.js on first export (see HistoryExportService.CopyViewerPage) and never overwritten
// again after that, so your edits here persist across future match syncs. Fill in functionUrl to show
// the "Wake match server" button on the results page; leave it empty to hide that section entirely.
window.WAKE_CONFIG = {
  // Function URL of the deploy/ec2-start-lambda Lambda (see deploy/README.md).
  functionUrl: "",

  // Only needed if you set SHARED_TOKEN on the Lambda. Note this is visible to anyone viewing this
  // page's source -- it's a deterrent against random scanners, not real access control. Real safety
  // comes from the Lambda's IAM role only being able to Start/Describe this one instance.
  token: "",

  // The instance's Tailscale IP and the app's port, so the page can link straight to the app once
  // it reports "running". Only reachable from devices enrolled in your own Tailscale network.
  tailscaleIp: "",
  port: 5000
};
