[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$privateFile = Join-Path $root '.env.docker.host-sql.local'
if (!(Test-Path -LiteralPath $privateFile)) { throw 'Run the approved administrator setup first.' }
if ((Get-Item -LiteralPath $privateFile).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'Private configuration is a reparse point; stopped.'
}
$passwordMatches = [regex]::Matches([IO.File]::ReadAllText($privateFile), '(?m)^HOST_SQL_PASSWORD=(Local!a7[0-9a-f]{32})\r?$')
if ($passwordMatches.Count -ne 1) { throw 'Generated SQL password is missing/invalid; no secrets were printed.' }
$previous = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
try {
    # Pass only the SQL password via an inherited environment variable, never CLI text.
    $env:SQLCMDPASSWORD = $passwordMatches[0].Groups[1].Value
    $query = @'
SET NOCOUNT ON;
IF DB_NAME() <> N'Project1Db' THROW 50000, 'Unexpected target database.', 1;
IF IS_SRVROLEMEMBER(N'sysadmin') <> 0 OR IS_MEMBER(N'db_owner') <> 0 OR HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER') <> 0 THROW 50000, 'Runtime login permissions are too broad.', 1;
IF IS_MEMBER(N'db_datareader') <> 1 OR IS_MEMBER(N'db_datawriter') <> 1 THROW 50000, 'Runtime data permissions are missing.', 1;
IF EXISTS (SELECT 1 FROM dbo.EmailOutboxes WHERE Status=N'Pending') THROW 50000, 'Pending business emails exist. Review before API startup.', 1;
SELECT DB_NAME() AS database_name, MAX(MigrationId) AS latest_migration FROM dbo.__EFMigrationsHistory;
'@
    docker run --rm --env SQLCMDPASSWORD --entrypoint /opt/mssql-tools18/bin/sqlcmd mcr.microsoft.com/mssql/server:2022-latest -S tcp:host.docker.internal,1433 -U project1_docker_local -d Project1Db -C -b -I -x -l 5 -t 10 -W -s '|' -Q $query
    if ($LASTEXITCODE) {
        throw 'Docker-to-Windows SQL check failed. Do not open LAN/public ports, rerun migrations, or start the new API; report the error.'
    }
    Write-Output 'Read-only Docker-to-Windows SQL check passed. No SQL Server container/service was started.'
} finally {
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $previous, 'Process')
}
