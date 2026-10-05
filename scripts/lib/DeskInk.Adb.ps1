Set-StrictMode -Version Latest

function Resolve-DeskInkAdb {
    $command = Get-Command adb -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw 'ADB não foi encontrado no PATH. Instale Android Platform Tools e abra um novo terminal.'
    }
    return $command.Source
}

function Get-DeskInkAdbDevices {
    param([Parameter(Mandatory = $true)][string]$Adb)

    $lines = & $Adb devices -l 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao consultar ADB: $($lines -join [Environment]::NewLine)"
    }

    $devices = @()
    foreach ($line in $lines) {
        if ($line -match '^([^\s]+)\s+(device|unauthorized|offline)(?:\s|$)') {
            $devices += [pscustomobject]@{
                Serial = $Matches[1]
                State = $Matches[2]
                Detail = $line
            }
        }
    }
    return $devices
}

function Resolve-DeskInkDevice {
    param(
        [Parameter(Mandatory = $true)][string]$Adb,
        [string]$Device
    )

    $requested = $Device
    if ([string]::IsNullOrWhiteSpace($requested)) {
        $requested = $env:ANDROID_SERIAL
    }

    $devices = @(Get-DeskInkAdbDevices -Adb $Adb)
    if (-not [string]::IsNullOrWhiteSpace($requested)) {
        $match = @($devices | Where-Object { $_.Serial -eq $requested })
        if ($match.Count -eq 0) {
            throw "Dispositivo ADB '$requested' não foi encontrado. Conectados: $($devices.Serial -join ', ')"
        }
        if ($match[0].State -ne 'device') {
            throw "Dispositivo '$requested' está em estado '$($match[0].State)'. Autorize o USB debugging no tablet."
        }
        return $match[0].Serial
    }

    $authorized = @($devices | Where-Object { $_.State -eq 'device' })
    if ($authorized.Count -eq 0) {
        $problemStates = $devices | ForEach-Object { "$($_.Serial)=$($_.State)" }
        throw "Nenhum dispositivo ADB autorizado. Estados: $($problemStates -join ', ')"
    }
    if ($authorized.Count -gt 1) {
        throw "Mais de um dispositivo autorizado: $($authorized.Serial -join ', '). Use -Device ou ANDROID_SERIAL."
    }
    return $authorized[0].Serial
}

function Invoke-DeskInkAdb {
    param(
        [Parameter(Mandatory = $true)][string]$Adb,
        [Parameter(Mandatory = $true)][string]$Device,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $Adb -s $Device @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "ADB falhou para '$Device': adb $($Arguments -join ' ')"
    }
}

function Set-DeskInkAdbReverse {
    param(
        [Parameter(Mandatory = $true)][string]$Adb,
        [Parameter(Mandatory = $true)][string]$Device,
        [int]$ControlPort = 27183,
        [int]$InputPort = 27184
    )

    Invoke-DeskInkAdb -Adb $Adb -Device $Device -Arguments @(
        'reverse', "tcp:$ControlPort", "tcp:$ControlPort"
    )
    Invoke-DeskInkAdb -Adb $Adb -Device $Device -Arguments @(
        'reverse', "tcp:$InputPort", "tcp:$InputPort"
    )
}
