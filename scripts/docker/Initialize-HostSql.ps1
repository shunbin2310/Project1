[CmdletBinding()]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (!$Apply) {
    Write-Output 'Preview only: this script prepares BIN/MSSQLSERVER for local Docker access.'
    Write-Output 'With -Apply: back up Project1Db, generate private credentials, create a scoped SQL login,'
    Write-Output 'enable mixed authentication and loopback-only TCP 1433, then restart MSSQLSERVER.'
    Write-Output 'No migrations, data merge, Gmail delivery, or container startup. Review before applying.'
    return
}

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script in an Administrator PowerShell. No changes were made.'
}
if ([Environment]::MachineName -ine 'BIN') { throw 'This script is restricted to the approved BIN computer.' }

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$privateFile = Join-Path $root '.env.docker.host-sql.local'
$stateFile = Join-Path $root '.docker.host-sql-state.local.json'
$registry = 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQLServer'
$tcpPath = $registry + '\SuperSocketNetLib\Tcp'
$service = Get-Service -Name MSSQLSERVER
if ($service.Status -ne 'Running') { throw 'MSSQLSERVER must already be running before setup.' }
if (@($service.DependentServices | Where-Object { $_.Status -eq 'Running' }).Count) {
    throw 'A dependent SQL service is running. Stop and review its restart separately.'
}
if (Get-NetTCPConnection -State Listen -LocalPort 1433 -ErrorAction SilentlyContinue) {
    throw 'Port 1433 is already listening. Stop and review instead of changing another service.'
}
foreach ($path in @($privateFile, $stateFile)) {
    if ((Test-Path -LiteralPath $path) -and
        ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'A private setup file is a reparse point. Stop.'
    }
}
if (Test-Path -LiteralPath $stateFile) {
    throw 'A previous setup state exists. Do not repeat initialization or reset credentials; review it first.'
}

function New-AdminConnection {
    $connection = [Data.SqlClient.SqlConnection]::new(
        'Server=lpc:BIN;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5;Application Name=Project1HostSqlSetup')
    $connection.Open()
    return $connection
}

function Invoke-SqlScalar($Connection, [string]$Sql) {
    $command = $Connection.CreateCommand()
    try { $command.CommandText = $Sql; return $command.ExecuteScalar() }
    finally { $command.Dispose() }
}

function Protect-PrivateFile([string]$Path) {
    $acl = [Security.AccessControl.FileSecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.SetOwner($sid)
    foreach ($text in @($sid.Value, 'S-1-5-18', 'S-1-5-32-544')) {
        $identity = [Security.Principal.SecurityIdentifier]::new($text)
        $rule = [Security.AccessControl.FileSystemAccessRule]::new($identity, 'FullControl', 'Allow')
        [void]$acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function New-RandomHex([int]$Bytes) {
    $buffer = New-Object byte[] $Bytes
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    return [BitConverter]::ToString($buffer).Replace('-', '').ToLowerInvariant()
}

$connection = New-AdminConnection
try {
    if ((Invoke-SqlScalar $connection "SELECT IS_SRVROLEMEMBER(N'sysadmin');") -ne 1) {
        throw 'The Windows login must be a SQL sysadmin for this explicit setup.'
    }
    if ((Invoke-SqlScalar $connection "SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128));") -ine 'BIN') {
        throw 'Unexpected SQL Server; stopped.'
    }
    if ((Invoke-SqlScalar $connection "SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int);") -ne 1 -or
        (Get-ItemProperty -LiteralPath $tcpPath).Enabled -ne 0) {
        throw 'Authentication/TCP no longer matches the reviewed starting state. Stop and review.'
    }
    if ((Invoke-SqlScalar $connection "SELECT COUNT(*) FROM sys.server_principals WHERE name=N'project1_docker_local';") -ne 0 -or
        (Invoke-SqlScalar $connection "SELECT COUNT(*) FROM Project1Db.sys.database_principals WHERE name=N'project1_docker_local';") -ne 0) {
        throw 'The proposed SQL login/user already exists; it will not be overwritten.'
    }
    $history = $connection.CreateCommand()
    try {
        $history.CommandText = 'SELECT MigrationId FROM Project1Db.dbo.__EFMigrationsHistory ORDER BY MigrationId;'
        $reader = $history.ExecuteReader()
        $applied = @()
        try { while ($reader.Read()) { $applied += $reader.GetString(0) } }
        finally { $reader.Dispose() }
    } finally { $history.Dispose() }
    $expected = @(Get-ChildItem -LiteralPath (Join-Path $root 'backend\Project1.Api\Migrations') -Filter '*.cs' |
        Where-Object { $_.Name -notlike '*.Designer.cs' -and $_.Name -ne 'AppDbContextModelSnapshot.cs' } |
        ForEach-Object { $_.BaseName } | Sort-Object)
    if (@(Compare-Object $expected $applied).Count) { throw 'Migration history differs. Do not run migrations automatically.' }
    if ((Invoke-SqlScalar $connection "SELECT COUNT(*) FROM Project1Db.dbo.EmailOutboxes WHERE Status=N'Pending';") -ne 0) {
        throw 'Pending business emails exist. Review them before enabling real SMTP.'
    }

    $backupDir = [string](Invoke-SqlScalar $connection "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000));")
    if (!$backupDir -or ![IO.Path]::IsPathRooted($backupDir)) { throw 'SQL backup directory was not verified.' }
    $backup = Join-Path $backupDir ('Project1Db-before-host-docker-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N') + '.bak')
    $backupCommand = $connection.CreateCommand()
    try {
        $backupCommand.CommandTimeout = 120
        $backupCommand.CommandText = 'BACKUP DATABASE [Project1Db] TO DISK=@path WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM;'
        [void]$backupCommand.Parameters.Add('@path', [Data.SqlDbType]::NVarChar, 4000)
        $backupCommand.Parameters['@path'].Value = $backup
        [void]$backupCommand.ExecuteNonQuery()
    } finally { $backupCommand.Dispose() }
    Write-Output ('Backup checksum verification passed: ' + $backup)

    if (!(Test-Path -LiteralPath $privateFile)) {
        [void](New-Item -ItemType File -Path $privateFile)
        Protect-PrivateFile $privateFile
        [IO.File]::WriteAllText($privateFile,
            [IO.File]::ReadAllText((Join-Path $root '.env.docker.host-sql.example')), [Text.UTF8Encoding]::new($false))
    }
    Protect-PrivateFile $privateFile
    $content = [IO.File]::ReadAllText($privateFile)
    $secrets = @{}
    foreach ($key in @('HOST_SQL_PASSWORD', 'JWT_SIGNING_KEY', 'NEW_USER_PASSWORD')) {
        $keyMatches = [regex]::Matches($content, '(?m)^' + $key + '=([^\r\n]*)\r?$')
        if ($keyMatches.Count -ne 1) { throw 'Private credential keys are missing or duplicated; stopped.' }
        $value = $keyMatches[0].Groups[1].Value
        if (!$value) {
            $value = if ($key -eq 'JWT_SIGNING_KEY') { New-RandomHex 32 } else { 'Local!a7' + (New-RandomHex 16) }
            $content = $content.Replace($keyMatches[0].Value, $key + '=' + $value)
        }
        if ($key -eq 'JWT_SIGNING_KEY') {
            if ($value -cnotmatch '^[0-9a-f]{64}$') { throw 'Unexpected JWT key format; no values were printed.' }
        } elseif ($value -cnotmatch '^Local!a7[0-9a-f]{32}$') {
            throw 'Unexpected generated password format; no values were printed.'
        }
        $secrets[$key] = $value
    }
    [IO.File]::WriteAllText($privateFile, $content, [Text.UTF8Encoding]::new($false))

    $ips = @(Get-ChildItem -LiteralPath $tcpPath | Where-Object { $_.PSChildName -match '^IP[0-9]+$' } |
        ForEach-Object { Get-ItemProperty -LiteralPath $_.PSPath })
    $loopback = @($ips | Where-Object { $_.IpAddress -eq '127.0.0.1' })
    if ($loopback.Count -ne 1 -or $loopback[0].Active -ne 1) { throw 'Expected active loopback configuration not found.' }
    $state = [ordered]@{
        Server = 'BIN'; Service = 'MSSQLSERVER'; Backup = $backup; Completed = $false
        LoginMode = (Get-ItemProperty -LiteralPath $registry).LoginMode
        TcpEnabled = (Get-ItemProperty -LiteralPath $tcpPath).Enabled
        ListenOnAllIPs = (Get-ItemProperty -LiteralPath $tcpPath).ListenOnAllIPs
        IPs = @($ips | Select-Object PSChildName, Enabled, IpAddress, TcpPort, TcpDynamicPorts)
    }
    [void](New-Item -ItemType File -Path $stateFile)
    Protect-PrivateFile $stateFile
    [IO.File]::WriteAllText($stateFile, ($state | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))

    $provision = $connection.CreateCommand()
    try {
        $provision.CommandText = @'
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @sql nvarchar(max) = N'CREATE LOGIN [project1_docker_local] WITH PASSWORD=' + QUOTENAME(@password, '''') + N', DEFAULT_DATABASE=[Project1Db], CHECK_POLICY=ON, CHECK_EXPIRATION=OFF;';
EXEC sys.sp_executesql @sql;
USE [Project1Db];
CREATE USER [project1_docker_local] FOR LOGIN [project1_docker_local] WITH DEFAULT_SCHEMA=[dbo];
ALTER ROLE [db_datareader] ADD MEMBER [project1_docker_local];
ALTER ROLE [db_datawriter] ADD MEMBER [project1_docker_local];
COMMIT TRANSACTION;
'@
        [void]$provision.Parameters.Add('@password', [Data.SqlDbType]::NVarChar, 128)
        $provision.Parameters['@password'].Value = $secrets['HOST_SQL_PASSWORD']
        [void]$provision.ExecuteNonQuery()
    } finally { $provision.Dispose() }
} finally { $connection.Dispose() }

# Explicitly restrict the native SQL listener to loopback. Do not listen on LAN IPs.
# If Docker Desktop cannot reach loopback, stop and review; do not broaden this automatically.
try {
    Set-ItemProperty -LiteralPath $registry -Name LoginMode -Value 2
    Set-ItemProperty -LiteralPath $tcpPath -Name ListenOnAllIPs -Value 0
    foreach ($ip in $ips) { Set-ItemProperty -LiteralPath $ip.PSPath -Name Enabled -Value 0 }
    Set-ItemProperty -LiteralPath $loopback[0].PSPath -Name TcpPort -Value '1433'
    Set-ItemProperty -LiteralPath $loopback[0].PSPath -Name TcpDynamicPorts -Value ''
    Set-ItemProperty -LiteralPath $loopback[0].PSPath -Name Enabled -Value 1
    Set-ItemProperty -LiteralPath $tcpPath -Name Enabled -Value 1
    Restart-Service -Name MSSQLSERVER
    (Get-Service -Name MSSQLSERVER).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    $listeners = @()
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $listeners = @(Get-NetTCPConnection -State Listen -LocalPort 1433 -ErrorAction SilentlyContinue)
        if ($listeners.Count) { break }
        Start-Sleep -Milliseconds 500
    }
    $process = (Get-CimInstance Win32_Service -Filter "Name='MSSQLSERVER'").ProcessId
    if (!$listeners.Count -or @($listeners | Where-Object { $_.LocalAddress -ne '127.0.0.1' -or $_.OwningProcess -ne $process }).Count) {
        throw 'SQL listener is not restricted to the expected local interface. Stop and review.'
    }
    $check = [Data.SqlClient.SqlConnection]::new(
        'Server=tcp:127.0.0.1,1433;Database=Project1Db;User ID=project1_docker_local;Password=' + $secrets['HOST_SQL_PASSWORD'] + ';Encrypt=True;TrustServerCertificate=True;Connect Timeout=5')
    try {
        $check.Open()
        if ((Invoke-SqlScalar $check "SELECT IS_SRVROLEMEMBER(N'sysadmin');") -ne 0 -or
            (Invoke-SqlScalar $check "SELECT IS_MEMBER(N'db_owner');") -ne 0 -or
            (Invoke-SqlScalar $check "SELECT HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'ALTER');") -ne 0) {
            throw 'Runtime SQL login has unexpected administrative/schema permissions.'
        }
    } finally { $check.Dispose() }
    $state['Completed'] = $true
    [IO.File]::WriteAllText($stateFile, ($state | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
} catch {
    Write-Warning 'Setup did not complete. Do not start the host-SQL API or rerun initialization.'
    Write-Warning 'The protected state file records the original TCP/login-mode settings; review recovery before continuing.'
    throw
}

Write-Output 'Setup complete: mixed authentication, loopback TCP 1433, scoped SQL login.'
Write-Output 'Existing Windows authentication and database tables were preserved. No sa enablement or migration.'
Write-Output ('Private configuration: ' + $privateFile)
Write-Output 'Fill Gmail settings locally, then run Test-HostSqlConnection.ps1. No Gmail messages were sent.'
