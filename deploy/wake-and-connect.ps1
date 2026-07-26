<#
.SYNOPSIS
  Wakes the EC2 match server (if stopped), waits until it's running, and opens the app in your
  browser over Tailscale.

.DESCRIPTION
  Once Tailscale is installed on the EC2 instance and on this device (see deploy/README.md,
  "Restricting access to specific devices"), the app is reachable directly at the instance's
  Tailscale IP -- no SSH key, no port forwarding, no changing public IP to track. This script only
  handles the "is it awake yet" part; connecting is then just opening a URL.

.USAGE
  Copy wake-and-connect.config.example.json to wake-and-connect.config.json next to this script
  and fill in functionUrl / tailscaleIp. Then just run:
      .\wake-and-connect.ps1
#>

$ErrorActionPreference = 'Stop'

$configPath = Join-Path $PSScriptRoot 'wake-and-connect.config.json'
$examplePath = Join-Path $PSScriptRoot 'wake-and-connect.config.example.json'

if (-not (Test-Path $configPath)) {
    Copy-Item $examplePath $configPath
    Write-Host "Created wake-and-connect.config.json from the example -- fill in functionUrl and tailscaleIp, then run this script again." -ForegroundColor Yellow
    exit 1
}

$config = Get-Content $configPath -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($config.functionUrl) -or [string]::IsNullOrWhiteSpace($config.tailscaleIp)) {
    Write-Host "wake-and-connect.config.json is missing functionUrl or tailscaleIp -- fill them in and try again." -ForegroundColor Red
    exit 1
}

function Get-WakeStatus {
    $url = "$($config.functionUrl)?action=status"
    if ($config.token) { $url += "&token=$($config.token)" }
    return Invoke-RestMethod -Uri $url -Method Get
}

function Start-WakeInstance {
    $url = "$($config.functionUrl)?action=start"
    if ($config.token) { $url += "&token=$($config.token)" }
    Invoke-RestMethod -Uri $url -Method Get | Out-Null
}

Write-Host "Checking server state..." -ForegroundColor Cyan
$status = Get-WakeStatus
Write-Host "Current state: $($status.state)"

if ($status.state -ne 'running') {
    Write-Host "Starting the instance..." -ForegroundColor Cyan
    Start-WakeInstance

    $maxAttempts = 40   # ~200s
    for ($i = 0; $i -lt $maxAttempts; $i++) {
        Start-Sleep -Seconds 5
        $status = Get-WakeStatus
        Write-Host "  ($($i + 1)/$maxAttempts) state: $($status.state)"
        if ($status.state -eq 'running') { break }
    }

    if ($status.state -ne 'running') {
        Write-Host "Instance did not report 'running' in time -- check the AWS console." -ForegroundColor Red
        exit 1
    }

    # The OS/app inside the instance (and Tailscale itself) need a bit longer to finish starting
    # even after AWS reports the instance as "running".
    Write-Host "Instance is running. Giving it a moment to finish starting..." -ForegroundColor Cyan
    Start-Sleep -Seconds 15
}

$url = "http://$($config.tailscaleIp):$($config.port)"
Write-Host "Opening $url" -ForegroundColor Cyan
Start-Process $url
