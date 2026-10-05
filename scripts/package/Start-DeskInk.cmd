@echo off
title DeskInk
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-DeskInk.ps1"
if errorlevel 1 pause
