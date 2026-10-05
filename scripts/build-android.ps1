param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$androidRoot = Join-Path $PSScriptRoot '..\apps\android'
$wrapper = Join-Path $androidRoot 'gradlew.bat'
if (-not (Test-Path -LiteralPath $wrapper)) {
    throw "Gradle Wrapper ausente em '$wrapper'. Execute a preparação da toolchain antes do build."
}
if ([string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
    $jdk = Get-ChildItem -LiteralPath 'C:\Program Files\Eclipse Adoptium' -Directory -Filter 'jdk-17*' -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if ($null -eq $jdk) {
        throw 'JAVA_HOME não está configurado e nenhum Temurin JDK 17 foi encontrado.'
    }
    $env:JAVA_HOME = $jdk.FullName
}
if ([string]::IsNullOrWhiteSpace($env:ANDROID_HOME) -and [string]::IsNullOrWhiteSpace($env:ANDROID_SDK_ROOT)) {
    $defaultSdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
    if (-not (Test-Path -LiteralPath $defaultSdk)) {
        throw 'ANDROID_HOME/ANDROID_SDK_ROOT não está configurado e o SDK padrão não existe.'
    }
    $env:ANDROID_HOME = $defaultSdk
    $env:ANDROID_SDK_ROOT = $defaultSdk
}

# Some Windows package managers leave quoted PATH entries behind. Gradle's test
# worker treats those unmatched quotes as Java command-line delimiters.
$sdkRoot = $env:ANDROID_HOME
if ([string]::IsNullOrWhiteSpace($sdkRoot)) {
    $sdkRoot = $env:ANDROID_SDK_ROOT
}
$cleanPath = @(($env:Path -split ';') | ForEach-Object { $_.Trim().Trim('"') } | Where-Object { $_ })
$env:Path = (@(
    (Join-Path $env:JAVA_HOME 'bin')
    (Join-Path $sdkRoot 'platform-tools')
) + $cleanPath | Select-Object -Unique) -join ';'

$task = if ($Configuration -eq 'Release') { 'assembleRelease' } else { 'assembleDebug' }
& $wrapper --no-daemon $task -p $androidRoot
if ($LASTEXITCODE -ne 0) {
    throw "Android build falhou: $task"
}
