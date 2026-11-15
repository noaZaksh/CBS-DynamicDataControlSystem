# הקמת בסיס הנתונים (Windows / PowerShell). דורש sqlcmd.
param(
  [string]$Server = "localhost",
  [string]$SaUser = "sa",
  [Parameter(Mandatory = $true)][string]$SaPassword,
  [string]$AppPassword = "DcApp#2026!",
  [string]$ReaderPassword = "DcReader#2026!"
)
Push-Location $PSScriptRoot
sqlcmd -S $Server -U $SaUser -P $SaPassword -C -b -f 65001 -i 00_run_all.sql -v AppPassword="$AppPassword" ReaderPassword="$ReaderPassword"
Pop-Location
