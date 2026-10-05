param([string]$Device)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot\lib\DeskInk.Adb.ps1"

$adb = Resolve-DeskInkAdb
$serial = Resolve-DeskInkDevice -Adb $adb -Device $Device
& $adb -s $serial logcat --pid=$( & $adb -s $serial shell pidof com.deskink.android )
