<#
  Builds the NijMitra PWA and API into a single deployable folder for IIS.
  Usage (from the repository root):  .\deployment\scripts\publish.ps1 -Output C:\deploy\nijmitra
#>
param([string]$Output = ".\publish\nijmitra")
$ErrorActionPreference = "Stop"
Write-Host "1/4  Building the PWA (NIJMITRA.Web)…"
Push-Location NIJMITRA.Web
npm ci
npm run build
Pop-Location
Write-Host "2/4  Running tests (requires NIJMITRA_TEST_DB)…"
if ($env:NIJMITRA_TEST_DB) { dotnet test NIJMITRA.Tests -c Release } else { Write-Warning "NIJMITRA_TEST_DB not set — skipping integration tests." }
Write-Host "3/4  Publishing the API…"
dotnet publish NIJMITRA.API -c Release -o $Output
Write-Host "4/4  Copying PWA into wwwroot and web.config…"
New-Item -ItemType Directory -Force -Path "$Output\wwwroot" | Out-Null
Copy-Item -Recurse -Force NIJMITRA.Web\dist\* "$Output\wwwroot\"
Copy-Item -Force deployment\iis\web.config "$Output\web.config"
Write-Host "Done → $Output  (copy to the IIS site folder; keep App_Data and logs on the server)."
