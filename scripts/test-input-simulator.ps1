$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Some launchers provide both PATH and path. Start-Process rejects that duplicate
# environment block, so normalize it inside this test process before spawning.
$processPath = [Environment]::GetEnvironmentVariable('PATH')
Remove-Item Env:path -ErrorAction SilentlyContinue
$env:PATH = $processPath

$root = Split-Path -Parent $PSScriptRoot
$hostExecutable = Join-Path $root 'apps\windows\DeskInk.WindowsHost\bin\Debug\net10.0-windows\win-x64\DeskInk.WindowsHost.exe'
$simulatorExecutable = Join-Path $root 'apps\windows\DeskInk.InputSimulator\bin\Debug\net10.0\DeskInk.InputSimulator.exe'
$logDirectory = Join-Path $root 'artifacts\test-logs'
$stdoutLog = Join-Path $logDirectory 'input-simulator-host.stdout.log'
$stderrLog = Join-Path $logDirectory 'input-simulator-host.stderr.log'

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
Remove-Item -LiteralPath $stdoutLog, $stderrLog -Force -ErrorAction SilentlyContinue

foreach ($port in 27183, 27184) {
    $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port)
    try {
        $probe.Start()
    }
    catch [System.Net.Sockets.SocketException] {
        throw "DeskInk test port $port is already occupied. Stop the running host first."
    }
    finally {
        $probe.Stop()
    }
}

if (-not (Test-Path -LiteralPath $hostExecutable) -or -not (Test-Path -LiteralPath $simulatorExecutable)) {
    throw 'Build the Windows solution in Debug before running the input simulator test.'
}

$hostProcess = Start-Process $hostExecutable -ArgumentList '--headless' -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog

try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($hostProcess.HasExited) {
            throw "DeskInk host exited before becoming ready. See $stderrLog"
        }
        $hostText = Get-Content -LiteralPath $stdoutLog -Raw -ErrorAction SilentlyContinue
        if ($hostText -match 'Control listener: 127\.0\.0\.1:27183' -and
            $hostText -match 'Input listener:\s+127\.0\.0\.1:27184') {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 50
    }
    if (-not $ready) {
        throw 'DeskInk host did not open both USB listeners within 5 seconds.'
    }

    $simulatorSummaries = [System.Collections.Generic.List[string]]::new()
    foreach ($scenario in 'normal', 'duplicate', 'reorder', 'gap', 'missing-up') {
        $simulatorOutput = & $simulatorExecutable --moves 1000 --scenario $scenario 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Input simulator failed for ${scenario}:`n$($simulatorOutput -join [Environment]::NewLine)"
        }
        $simulatorText = $simulatorOutput -join [Environment]::NewLine
        $expectedUp = if ($scenario -eq 'missing-up') { 0 } else { 1 }
        if ($simulatorText -notmatch "SIMULATOR PASS scenario=$scenario .*down=1 move=1000 up=$expectedUp") {
            throw "Unexpected simulator summary for ${scenario}:`n$simulatorText"
        }
        $simulatorSummaries.Add($simulatorText)
    }

    foreach ($scenario in 'bad-bind', 'wrong-session', 'bad-version', 'truncated', 'invalid-sample-count') {
        $simulatorOutput = & $simulatorExecutable --moves 1 --scenario $scenario 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Rejection scenario failed for ${scenario}:`n$($simulatorOutput -join [Environment]::NewLine)"
        }
        $simulatorText = $simulatorOutput -join [Environment]::NewLine
        if ($simulatorText -notmatch "SIMULATOR REJECTION PASS scenario=$scenario") {
            throw "Unexpected rejection summary for ${scenario}:`n$simulatorText"
        }
        $simulatorSummaries.Add($simulatorText)
    }

    $timeoutOutput = & $simulatorExecutable --moves 1 --scenario timeout 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "USB watchdog scenario failed:`n$($timeoutOutput -join [Environment]::NewLine)"
    }
    $timeoutText = $timeoutOutput -join [Environment]::NewLine
    if ($timeoutText -notmatch
        'SIMULATOR PASS scenario=timeout framesSent=5 contactDuringPause=1 neutralRecovery=1') {
        throw "Unexpected USB watchdog summary:`n$timeoutText"
    }
    $simulatorSummaries.Add($timeoutText)

    $recoveryOutput = & $simulatorExecutable --moves 1 --scenario normal 2>&1
    if ($LASTEXITCODE -ne 0 -or
        ($recoveryOutput -join [Environment]::NewLine) -notmatch 'SIMULATOR PASS scenario=normal') {
        throw "Host did not recover after malformed connections:`n$($recoveryOutput -join [Environment]::NewLine)"
    }

    $reconnectCount = 25
    for ($attempt = 1; $attempt -le $reconnectCount; $attempt++) {
        $reconnectOutput = & $simulatorExecutable --moves 1 --scenario normal 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Reconnect storm failed at attempt ${attempt}:`n$($reconnectOutput -join [Environment]::NewLine)"
        }
    }

    $hostText = Get-Content -LiteralPath $stdoutLog -Raw
    $expectedHostSummaries = @(
        'frames=32 samples=1002 gaps=0 dup=0 reorder=0 release=None',
        'frames=32 samples=1002 gaps=0 dup=1 reorder=0 release=None',
        'frames=32 samples=1002 gaps=0 dup=0 reorder=1 release=None',
        'frames=32 samples=1002 gaps=1 dup=0 reorder=0 release=None',
        'frames=32 samples=1001 gaps=0 dup=0 reorder=0 release=Up',
        'frames=4 samples=4 gaps=1 dup=0 reorder=0 release=None'
    )
    foreach ($summary in $expectedHostSummaries) {
        if ($hostText -notmatch [regex]::Escape($summary)) {
            throw "Host did not confirm expected scenario '$summary':`n$hostText"
        }
    }
    $expectedErrors = @(
        'INPUT ERROR ProtocolException: Invalid input bind token',
        'INPUT ERROR ProtocolException: Input session mismatch',
        'INPUT ERROR ProtocolException: Unsupported protocol version: 2.0',
        'INPUT ERROR ProtocolException: Input frame length is outside limits: 10',
        'INPUT ERROR ProtocolException: Invalid sample count: 0',
        'USB INPUT WATCHDOG: outputs released; waiting for a neutral sample'
    )
    $stderrLines = @(Get-Content -LiteralPath $stderrLog -ErrorAction SilentlyContinue |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($expectedError in $expectedErrors) {
        if ($stderrLines -notcontains $expectedError) {
            throw "Host did not report expected rejection '$expectedError':`n$($stderrLines -join [Environment]::NewLine)"
        }
    }
    $unexpectedErrors = @($stderrLines | Where-Object { $_ -notin $expectedErrors })
    if ($unexpectedErrors.Count -gt 0) {
        throw "Host reported unexpected errors:`n$($unexpectedErrors -join [Environment]::NewLine)"
    }

    $closedSessions = ([regex]::Matches($hostText, 'INPUT CLOSED ')).Count
    $expectedSessions = 5 + 5 + 1 + 1 + $reconnectCount
    if ($closedSessions -ne $expectedSessions) {
        throw "Expected $expectedSessions closed input sessions, observed $closedSessions."
    }

    Write-Output $simulatorSummaries
    Write-Output "HOST PASS lifecycle faults=5 watchdog=1 reconnects=$reconnectCount sessions=$expectedSessions"
}
finally {
    if (-not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id
        $null = $hostProcess.WaitForExit(5000)
    }
    $hostProcess.Dispose()
}
