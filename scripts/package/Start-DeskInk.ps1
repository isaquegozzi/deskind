$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$hostExecutable = Join-Path $PSScriptRoot 'DeskInk.WindowsHost.exe'
if (-not (Test-Path -LiteralPath $hostExecutable)) {
    throw "DeskInk.WindowsHost.exe não foi encontrado ao lado deste iniciador."
}

Write-Host 'DeskInk 0.1.0'
Write-Host 'Mantenha esta janela aberta enquanto usar o tablet.'

$adb = Get-Command adb -ErrorAction SilentlyContinue
if ($null -ne $adb) {
    $deviceLines = @(& $adb.Source devices | Select-Object -Skip 1 | Where-Object { $_ -match "`tdevice$" })
    if ($deviceLines.Count -eq 1) {
        $serial = ($deviceLines[0] -split "`t")[0]
        & $adb.Source -s $serial reverse tcp:27183 tcp:27183 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Não foi possível configurar o canal USB de controle.' }
        & $adb.Source -s $serial reverse tcp:27184 tcp:27184 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Não foi possível configurar o canal USB de entrada.' }
        Write-Host "USB pronto: $serial"
    }
    elseif ($deviceLines.Count -gt 1) {
        Write-Warning 'Mais de um Android conectado. Use LAN ou deixe apenas o tablet desejado no USB.'
    }
    else {
        Write-Host 'Nenhum tablet ADB detectado; a conexão LAN continua disponível.'
    }
}
else {
    Write-Host 'ADB não encontrado; a conexão LAN continua disponível.'
}

& $hostExecutable --enable-all --enable-lan --ui
