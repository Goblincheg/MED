param([string]$IsccPath)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    $version = '1.0.0'
    $releaseName = "MED-$version-win-x64"
    $releaseRoot = Join-Path $projectRoot 'artifacts\releases'
    $payloadRoot = Join-Path $projectRoot "publish\MED-$version"
    $installed = Join-Path $payloadRoot 'installed'
    $portable = Join-Path $payloadRoot 'portable'
    New-Item $releaseRoot,$installed,$portable -ItemType Directory -Force | Out-Null
    if ((Test-Path (Join-Path $installed 'Data')) -or (Test-Path (Join-Path $installed 'portable.txt'))) {
        throw 'Installed staging contains portable/user data. Choose a clean staging directory; nothing was deleted.'
    }
    & dotnet build tests/EmojiPicker.Checks/EmojiPicker.Checks.csproj -c Release -r win-x64 --self-contained true -p:BaseOutputPath=bin/med100/ --verbosity minimal
    if ($LASTEXITCODE) { throw 'Checks build failed.' }
    $checks = Join-Path $projectRoot 'tests\EmojiPicker.Checks\bin\med100\Release\net10.0-windows\win-x64\EmojiPicker.Checks.exe'
    & $checks
    if ($LASTEXITCODE) { throw 'Automated checks failed.' }
    foreach ($mode in @('installed','portable')) {
        $testRoot = Join-Path $projectRoot ("tests\EmojiPicker.Checks\obj\release-startup-" + [guid]::NewGuid().ToString('N'))
        & $checks --startup-check $mode $testRoot --background
        if ($LASTEXITCODE) { throw "Startup checks failed: $mode" }
        if ($mode -eq 'installed') {
            $shutdownPid = [int](Get-Content (Join-Path $testRoot 'shutdown-child.pid'))
            # The helper may exit between discovery and waiting. Holding the
            # Process object avoids the Get-Process/Wait-Process ID lookup race.
            try {
                $shutdownProcess = [System.Diagnostics.Process]::GetProcessById($shutdownPid)
                if (!$shutdownProcess.WaitForExit(15000)) { throw 'Shutdown helper timed out.' }
            } catch [System.ArgumentException] { } # already exited
            if ((Get-Content (Join-Path $testRoot 'shutdown-result.txt')) -ne '0') { throw 'Graceful shutdown command failed.' }
        }
    }
    & dotnet publish EmojiPicker/EmojiPicker.csproj -c Release -r win-x64 --self-contained true --no-restore -p:BaseOutputPath=bin/med100/ -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false -o $installed --verbosity minimal
    if ($LASTEXITCODE) { throw 'Publication failed.' }
    Copy-Item (Join-Path $projectRoot 'docs\MED-1.0.0-README.txt') (Join-Path $installed 'README-MED.txt') -Force
    $files = @(Get-ChildItem $installed -File -Recurse)
    if ($files.Name -contains 'EmojiPicker.exe') { throw 'Unexpected old executable in staging; nothing was deleted.' }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($installed.Length + 1)
        $destination = Join-Path $portable $relative
        New-Item (Split-Path $destination -Parent) -ItemType Directory -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    }
    Set-Content (Join-Path $portable 'portable.txt') 'MED portable: use Data beside MED.exe; never enable autostart automatically.' -Encoding UTF8

    # Package only the known payload list, never a local Data directory, logs or settings.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipPath = Join-Path $releaseRoot "$releaseName-portable.zip"
    $temporaryZip = Join-Path $releaseRoot ([guid]::NewGuid().ToString('N') + '.zip')
    $archive = [System.IO.Compression.ZipFile]::Open($temporaryZip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in @($files | ForEach-Object { $_.FullName.Substring($installed.Length + 1) }) + @('portable.txt')) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $portable $relative), $relative.Replace('\','/'), [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    Move-Item -LiteralPath $temporaryZip -Destination $zipPath -Force

    if (!$IsccPath) {
        foreach ($candidate in @(
            "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe")) {
            if (!$IsccPath -and (Test-Path -LiteralPath $candidate)) { $IsccPath = $candidate }
        }
        if (!$IsccPath) {
            $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($command) { $IsccPath = $command.Source }
        }
    }
    $setupPath = $null
    if ($IsccPath) {
        & $IsccPath "/DAppVersion=$version" "/DPublishDir=$installed" "/O$releaseRoot" installer/EmojiPicker.iss
        if ($LASTEXITCODE) { throw 'Inno Setup compilation failed.' }
        $setupPath = Join-Path $releaseRoot "$releaseName-setup.exe"
        if (!(Test-Path $setupPath)) { throw 'Installer compiler did not produce the expected EXE.' }
    } else {
        Write-Warning 'ISCC.exe not found. Setup EXE is BLOCKED; no tools were downloaded or installed.'
    }
    $summary = [ordered]@{
        Version = $version
        PortableZip = $zipPath
        PortableZipBytes = (Get-Item $zipPath).Length
        PortableExe = Join-Path $portable 'MED.exe'
        InstalledPayload = $installed
        InstalledPayloadBytes = ($files | Measure-Object Length -Sum).Sum
        SetupExe = $setupPath
        SetupStatus = $(if ($setupPath) { 'compiled-not-installed' } else { 'blocked-ISCC-not-found' })
        SetupBytes = $(if ($setupPath) { (Get-Item $setupPath).Length } else { $null })
    }
    $summary | ConvertTo-Json | Set-Content (Join-Path $releaseRoot 'MED-1.0.0-build.json') -Encoding UTF8
    Get-FileHash $zipPath -Algorithm SHA256 | Format-List
    $summary | ConvertTo-Json
} finally { Pop-Location }
