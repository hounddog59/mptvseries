# Copies the MP-TVSeries.dll you just built in Visual Studio into MediaPortal, safely.
# Run it with Install-to-MediaPortal.cmd (in the folder above), which asks for administrator rights.
#
# - uses whichever build is newest (Release or Debug)
# - waits until MediaPortal and its Configuration are closed
# - keeps a backup of the plugin it replaces, in the "InstalledBackups" folder next to this project
# - checks the copy arrived intact

$ErrorActionPreference = 'Stop'

function Finish($message, $ok) {
    Write-Host ''
    if ($ok) { Write-Host $message -ForegroundColor Green } else { Write-Host $message -ForegroundColor Red }
    Write-Host ''
    Read-Host 'Press Enter to close'
    if ($ok) { exit 0 } else { exit 1 }
}

try {
    $projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
    $binFolder = Join-Path $projectRoot 'MP-TVSeries\bin'

    # the newest MP-TVSeries.dll that Visual Studio built
    $built = @()
    if (Test-Path $binFolder) {
        $built = @(Get-ChildItem -Path $binFolder -Recurse -Filter 'MP-TVSeries.dll' | Sort-Object LastWriteTime -Descending)
    }
    if ($built.Count -eq 0) {
        Finish "No built MP-TVSeries.dll found in $binFolder`nBuild the solution in Visual Studio first (Build > Build Solution)." $false
    }
    $source = $built[0]
    Write-Host ("Built plugin:  {0}" -f $source.FullName)
    Write-Host ("Built at:      {0}" -f $source.LastWriteTime)
    if ($source.LastWriteTime -lt (Get-Date).AddHours(-12)) {
        Write-Host 'Note: this build is more than 12 hours old - did the latest build succeed?' -ForegroundColor Yellow
    }

    # MediaPortal's folder
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Team MediaPortal\MediaPortal'),
        (Join-Path ${env:ProgramFiles(x86)} 'Team MediaPortal\MediaPortal')
    )
    $mediaPortal = $candidates | Where-Object { $_ -and (Test-Path (Join-Path $_ 'plugins\Windows')) } | Select-Object -First 1
    if (-not $mediaPortal) {
        Finish "Could not find MediaPortal's plugins\Windows folder in Program Files." $false
    }
    $target = Join-Path $mediaPortal 'plugins\Windows\MP-TVSeries.dll'

    # MediaPortal and its Configuration lock the plugin while they're open
    while ($true) {
        $running = @(Get-Process -Name 'MediaPortal', 'Configuration' -ErrorAction SilentlyContinue)
        if ($running.Count -eq 0) { break }
        Write-Host ''
        Write-Host ('Please close: ' + (($running | ForEach-Object { $_.ProcessName + '.exe' } | Sort-Object -Unique) -join ', ')) -ForegroundColor Yellow
        Read-Host 'Then press Enter to continue'
    }

    # back up the plugin being replaced
    if (Test-Path $target) {
        $backupFolder = Join-Path $projectRoot 'InstalledBackups'
        New-Item -ItemType Directory -Force -Path $backupFolder | Out-Null
        $backup = Join-Path $backupFolder ('MP-TVSeries-' + (Get-Item $target).LastWriteTime.ToString('yyyy-MM-dd_HHmm') + '.dll')
        Copy-Item -LiteralPath $target -Destination $backup -Force
        Write-Host ("Backup of old: {0}" -f $backup)
    }

    Copy-Item -LiteralPath $source.FullName -Destination $target -Force

    $sourceHash = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash
    $targetHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($sourceHash -ne $targetHash) {
        Finish "The copy did not arrive intact. Nothing else was changed - try running this again." $false
    }

    Finish ("Installed. MediaPortal now has the plugin built at {0}.`n{1}" -f $source.LastWriteTime, $target) $true
}
catch {
    Finish ("Something went wrong: " + $_.Exception.Message) $false
}
