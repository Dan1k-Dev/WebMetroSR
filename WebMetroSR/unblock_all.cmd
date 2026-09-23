@echo off
powershell -ExecutionPolicy Bypass -Command "& {$t = Join-Path $env:USERPROFILE 'AppData\Local\Temp\WebMetroSR'; if (Test-Path $t) { Get-ChildItem -Path $t -Recurse -File | Unblock-File; Write-Host 'All files unblocked.' } else { Write-Host 'Folder not found.' }}"
pause