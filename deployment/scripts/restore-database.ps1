<# Restores a backup created by backup-database.ps1. Stop the IIS site first. #>
param([Parameter(Mandatory)][string]$DumpFile, [string]$FilesZip, [string]$PgHost = "localhost", [int]$Port = 5432,
      [string]$Database = "nijmitraapp", [string]$User = "postgres", [string]$SiteRoot = "C:\inetpub\nijmitra")
$ErrorActionPreference = "Stop"
& pg_restore -h $PgHost -p $Port -U $User -d $Database --clean --if-exists $DumpFile
if ($FilesZip) { Expand-Archive -Path $FilesZip -DestinationPath "$SiteRoot\App_Data" -Force }
Write-Host "Restore complete. Start the IIS site again."
