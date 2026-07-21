$ErrorActionPreference = "Stop"

# Builds the Release WebAssembly bundle and serves it locally - same content the
# GitHub Pages deploy produces (including the per-version savestate zips). Requires Python 3.

$port = 8080
$site = "$PSScriptRoot/src/AutoLUT.Browser/bin/Release/net10.0-browser/publish/wwwroot"

Write-Host "Publishing browser app (Release)..."
dotnet publish "$PSScriptRoot/src/AutoLUT.Browser" -c Release | Out-Host
if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "Bundling per-version savestate zips..."
Compress-Archive `
    -Path "$PSScriptRoot/savestates/lut_gzs_1.0" `
    -DestinationPath "$site/savestates-1.0.zip" `
    -Force
Compress-Archive `
    -Path "$PSScriptRoot/savestates/lut_gzs_1.2" `
    -DestinationPath "$site/savestates-1.2.zip" `
    -Force

# Wii app zip is served next to the savestates; reuse a boot.dol from a prior
# ./build.ps1 --wii run rather than requiring Docker just to preview the site.
if (Test-Path "$PSScriptRoot/wii/boot.dol") {
    Write-Host "Bundling AutoLUT Palette zip..."
    $hbcApp = "$PSScriptRoot/build/serve-web-hbc/apps/autolut-palette"
    New-Item -ItemType Directory -Path $hbcApp -Force | Out-Null
    Copy-Item "$PSScriptRoot/wii/boot.dol", "$PSScriptRoot/wii/meta.xml" -Destination $hbcApp
    Compress-Archive `
        -Path "$PSScriptRoot/build/serve-web-hbc/apps" `
        -DestinationPath "$site/AutoLUT-Palette.zip" `
        -Force
} else {
    Write-Host "Skipping AutoLUT Palette zip (wii/boot.dol not built; run ./build.ps1 --wii first)"
}

if (Test-Path "$PSScriptRoot/n64/autolut-palette.z64") {
    Write-Host "Bundling AutoLUT Palette N64 ROM..."
    Copy-Item "$PSScriptRoot/n64/autolut-palette.z64" -Destination "$site/autolut-palette.z64" -Force
} else {
    Write-Host "Skipping AutoLUT Palette N64 ROM (n64/autolut-palette.z64 not built; run ./build.ps1 --n64 first)"
}

Write-Host "Serving at http://localhost:$port (Ctrl+C to stop)"

$python = @'
import http.server, webbrowser, sys

port = int(sys.argv[1])
http.server.SimpleHTTPRequestHandler.extensions_map[".wasm"] = "application/wasm"
http.server.SimpleHTTPRequestHandler.extensions_map[".js"] = "text/javascript"
http.server.SimpleHTTPRequestHandler.extensions_map[".json"] = "application/json"

server = http.server.HTTPServer(("127.0.0.1", port), http.server.SimpleHTTPRequestHandler)
webbrowser.open(f"http://localhost:{port}/")
server.serve_forever()
'@

# Passed as a file: inline -c strings lose their quotes to PowerShell's native-arg handling.
$serveScript = Join-Path ([System.IO.Path]::GetTempPath()) "autolut-serve-web.py"
Set-Content -Path $serveScript -Value $python

Push-Location $site
try {
    python $serveScript $port
}
finally {
    Pop-Location
}
