<#
  Daily backup of the NijMitra PostgreSQL database and private file storage.
  Schedule with Windows Task Scheduler. Requires pg_dump on PATH and PGPASSWORD (or a pgpass.conf file).
#>
param(
  [string]$PgHost = "localhost", [int]$Port = 5432, [string]$Database = "nijmitraapp", [string]$User = "postgres",
  [string]$SiteRoot = "C:\inetpub\nijmitra", [string]$BackupRoot = "D:\Backups\nijmitra", [int]$KeepDays = 30
)
$ErrorActionPreference = "Stop"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
& pg_dump -h $PgHost -p $Port -U $User -F c -b -f "$BackupRoot\db-$stamp.dump" $Database
Compress-Archive -Path "$SiteRoot\App_Data\*" -DestinationPath "$BackupRoot\files-$stamp.zip" -Force
Get-ChildItem $BackupRoot -File | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$KeepDays) } | Remove-Item
Write-Host "Backup complete: $stamp"
