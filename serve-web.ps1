$ErrorActionPreference = "Stop"

# Builds the Release WebAssembly bundle and serves it locally - same content the
# GitHub Pages deploy produces (including savestates.zip). Requires Python 3.

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
