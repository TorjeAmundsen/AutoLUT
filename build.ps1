$ErrorActionPreference = "Stop"

$project = "src/AutoLUT.App/AutoLUT.App.csproj"
$dockerImage = "autolut-build"

$rids = @("win-x64", "linux-x64")

$debug = $args -contains "--debug"

$csprojVersion = ([xml](Get-Content "$PSScriptRoot/Directory.Build.props")).Project.PropertyGroup.Version
$appVersion = "v$csprojVersion"

function Build($rid) {
    $output = "$PSScriptRoot/build/$rid"

    if (Test-Path $output) { Remove-Item $output -Recurse -Force }

    Write-Host "Building AutoLUT ($rid)..."

    # Native AOT requires building on the target OS.
    # Use Docker for Linux AOT builds when running on Windows.
    $currentOs = if ($IsWindows) { "win" } elseif ($IsLinux) { "linux" } else { "osx" }
    $needsDocker = -not $rid.StartsWith($currentOs)

    if ($needsDocker -and $rid.StartsWith("linux")) {
        Write-Host "  Using Docker for cross-OS AOT compilation..."

        docker build -t $dockerImage "$PSScriptRoot" | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Docker image build failed with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
        }

        $dockerPublishArgs = "dotnet publish $project -c Release -r $rid --self-contained true /p:PublishAot=true -o /src/build/$rid"
        if ($debug) {
            $dockerPublishArgs += " /p:NativeDebugSymbols=true /p:StripSymbols=false"
        }

        docker run --rm `
            -v "${PSScriptRoot}:/src" `
            -w /src `
            $dockerImage `
            sh -c $dockerPublishArgs `
            | Out-Host

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Docker build failed for $rid with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
        }

        # Remove debug symbols from output (.pdb on Windows, .dbg from Linux StripSymbols)
        Get-ChildItem "$output" -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item
        Get-ChildItem "$output" -Filter "*.dbg" -ErrorAction SilentlyContinue | Remove-Item

        CopySavestates $output
        Write-Host "  Output: $output"
        return $output
    }

    $dotnetArgs = @(
        "publish", $project,
        "-c", "Release",
        "-r", $rid,
        "--self-contained", "true",
        "/p:PublishAot=true",
        "-o", $output
    )

    if ($debug) {
        $dotnetArgs += "/p:NativeDebugSymbols=true"
        $dotnetArgs += "/p:StripSymbols=false"
    }

    dotnet @dotnetArgs | Out-Host

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $rid with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    # Remove debug symbols from output (unless --debug specified)
    if (-not $debug) {
        Get-ChildItem "$output" -Filter "*.pdb" -ErrorAction SilentlyContinue | Remove-Item
        Get-ChildItem "$output" -Filter "*.dbg" -ErrorAction SilentlyContinue | Remove-Item
    }

    CopySavestates $output
    Write-Host "  Output: $output"
    return $output
}

function CopySavestates($outputPath) {
    New-Item -ItemType Directory -Path "$outputPath/savestates" -Force | Out-Null
    Copy-Item -Path "$PSScriptRoot/savestates/lut_gzs_*" -Destination "$outputPath/savestates" -Recurse
}

function ZipBuild($outputPath, $rid) {
    $zipName = "$PSScriptRoot/build/AutoLUT-$appVersion-$rid.zip"

    if (Test-Path $zipName) { Remove-Item $zipName }

    Compress-Archive -Path "$outputPath/*" -DestinationPath $zipName
    Write-Host "  Zipped: $zipName"
}

function ZipSavestates() {
    $zipName = "$PSScriptRoot/build/savestates-$appVersion.zip"

    if (Test-Path $zipName) { Remove-Item $zipName }

    Compress-Archive -Path "$PSScriptRoot/savestates/lut_gzs_*" -DestinationPath $zipName
    Write-Host "  Zipped: $zipName"
}

function BuildWii() {
    Write-Host "Building AutoLUT Palette (Wii)..."

    docker run --rm `
        -v "${PSScriptRoot}:/src" `
        -w /src/wii `
        devkitpro/devkitppc:latest `
        make `
        | Out-Host

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Wii homebrew build failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    $appFolder = "$PSScriptRoot/build/wii-hbc/apps/autolut-palette"
    if (Test-Path "$PSScriptRoot/build/wii-hbc") { Remove-Item "$PSScriptRoot/build/wii-hbc" -Recurse -Force }
    New-Item -ItemType Directory -Path $appFolder -Force | Out-Null
    Copy-Item "$PSScriptRoot/wii/boot.dol", "$PSScriptRoot/wii/meta.xml" -Destination $appFolder

    $zipName = "$PSScriptRoot/build/AutoLUT-Palette-$appVersion.zip"
    if (Test-Path $zipName) { Remove-Item $zipName }
    Compress-Archive -Path "$PSScriptRoot/build/wii-hbc/apps" -DestinationPath $zipName
    Write-Host "  Zipped: $zipName"
}

if ($args -contains "--wii") {
    if (-not (Test-Path "$PSScriptRoot/build")) { New-Item -ItemType Directory -Path "$PSScriptRoot/build" | Out-Null }
    BuildWii
    Write-Host "Build complete."
    exit 0
}

if ($args -contains "--savestates") {
    if (-not (Test-Path "$PSScriptRoot/build")) { New-Item -ItemType Directory -Path "$PSScriptRoot/build" | Out-Null }
    ZipSavestates
    Write-Host "Build complete."
    exit 0
}

if ($args -contains "--all") {
    foreach ($rid in $rids) {
        $out = Build $rid
        ZipBuild $out $rid
    }
    ZipSavestates
    BuildWii
    Write-Host "Build complete."
    exit 0
}

# Interactive menu
Write-Host "Select target:"
for ($i = 0; $i -lt $rids.Count; $i++) {
    Write-Host "  [$($i + 1)] $($rids[$i])"
}
Write-Host "  [$($rids.Count + 1)] wii (AutoLUT Palette homebrew)"
$ridChoice = Read-Host "Choice"

if ([int]$ridChoice -eq $rids.Count + 1) {
    if (-not (Test-Path "$PSScriptRoot/build")) { New-Item -ItemType Directory -Path "$PSScriptRoot/build" | Out-Null }
    BuildWii
} else {
    $rid = $rids[[int]$ridChoice - 1]
    Build $rid
}
Write-Host "Build complete."
