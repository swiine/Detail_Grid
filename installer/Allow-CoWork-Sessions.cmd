@echo off
rem Lets other PCs connect to CoWork sessions hosted on this PC (TCP port 5392), and to the office server
rem (TCP 5391) if this PC runs it. Needs admin: it asks for it. Run once per PC, or have IT push the same rule.
net session >nul 2>&1
if errorlevel 1 (
  echo Asking for admin rights...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "$ErrorActionPreference='SilentlyContinue';" ^
 "$blocks = Get-NetFirewallRule -Direction Inbound -Action Block | Where-Object { ($_ | Get-NetFirewallApplicationFilter).Program -like '*\acad.exe' };" ^
 "if ($blocks) { $blocks | Remove-NetFirewallRule; Write-Host ('Removed ' + @($blocks).Count + ' rule(s) that were blocking Civil 3D.') };" ^
 "Get-NetFirewallRule -DisplayName 'CoWork sessions' | Remove-NetFirewallRule;" ^
 "New-NetFirewallRule -DisplayName 'CoWork sessions' -Description 'CoWork live drafting sessions and server' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5391,5392 -Profile Domain,Private | Out-Null;" ^
 "if (Get-NetFirewallRule -DisplayName 'CoWork sessions') { Write-Host 'Done: CoWork sessions are allowed through the firewall on this PC.' -ForegroundColor Green } else { Write-Host 'Could not add the rule - your IT may manage the firewall centrally. Ask them to allow inbound TCP 5391-5392.' -ForegroundColor Yellow }"
echo.
pause
