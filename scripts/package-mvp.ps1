param(
    [string]$Version = '0.1.0',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$packageName = "DeskInk-$Version"
$packageRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot $packageName))
$zipPath = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "$packageName-win-x64.zip"))
$installerPath = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "DeskInk-Setup-$Version-win-x64.exe"))

if (-not $packageRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'O diretório calculado do pacote saiu de artifacts.'
}
if (Test-Path -LiteralPath $packageRoot) {
    if (-not $Force) { throw "'$packageRoot' já existe. Use -Force para recriar somente este pacote." }
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    if (-not $Force) { throw "'$zipPath' já existe. Use -Force para recriar somente este pacote." }
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path -LiteralPath $installerPath) {
    if (-not $Force) { throw "'$installerPath' já existe. Use -Force para recriar somente este instalador." }
    Remove-Item -LiteralPath $installerPath -Force
}

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

$hostProject = Join-Path $repositoryRoot 'apps\windows\DeskInk.WindowsHost\DeskInk.WindowsHost.csproj'
& dotnet publish $hostProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --source 'https://api.nuget.org/v3/index.json' `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $packageRoot
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o host Windows.' }

& (Join-Path $PSScriptRoot 'build-android.ps1') -Configuration Release
$apkSource = Join-Path $repositoryRoot 'apps\android\app\build\outputs\apk\release\app-release.apk'
if (-not (Test-Path -LiteralPath $apkSource)) { throw "APK assinado não encontrado em '$apkSource'." }
Copy-Item -LiteralPath $apkSource -Destination (Join-Path $packageRoot "DeskInk-Android-$Version.apk")

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\install-and-troubleshooting.md') -Destination (Join-Path $packageRoot 'LEIA-ME.md')

Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal

$innoCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$innoCompiler = $innoCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $innoCompiler) {
    throw 'Inno Setup 6 não encontrado. Instale JRSoftware.InnoSetup via winget.'
}
$installerScript = Join-Path $PSScriptRoot 'package\DeskInk.iss'
& $innoCompiler `
    "/DPackageRoot=$packageRoot" `
    "/DOutputDirectory=$artifactsRoot" `
    "/DAppVersion=$Version" `
    $installerScript
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $installerPath)) {
    throw 'Falha ao criar o instalador Windows.'
}

Write-Host "Pacote criado: $packageRoot"
Write-Host "ZIP criado:    $zipPath"
Write-Host "Instalador:    $installerPath"
