[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^/home/shunbin/project1-production-backup-download\.[0-9a-f]{16}/Project1Db\.bak$')]
    [string]$RemoteFile,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedSha256,
    [Parameter(Mandatory=$true)][ValidateRange(1,[long]::MaxValue)][long]$ExpectedBytes
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = Join-Path $env:USERPROFILE 'Project1Backups'
if (Test-Path -LiteralPath $Root) {
    $Item = Get-Item -LiteralPath $Root
    if (!$Item.PSIsContainer -or ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Backup root must be an ordinary folder.'
    }
} else { New-Item -ItemType Directory -Path $Root | Out-Null }
$Folder = Join-Path $Root ('ProductionCutover-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $Folder | Out-Null
$Acl = Get-Acl -LiteralPath $Folder
$Acl.SetAccessRuleProtection($true, $false)
$Current = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
foreach ($Value in @($Current, 'S-1-5-18', 'S-1-5-32-544')) {
    $Sid = [Security.Principal.SecurityIdentifier]::new($Value)
    $Rule = [Security.AccessControl.FileSystemAccessRule]::new(
        $Sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $Acl.SetAccessRule($Rule)
}
Set-Acl -LiteralPath $Folder -AclObject $Acl
$File = Join-Path $Folder 'Project1Db-before-docker.bak'
$Remote = 'shunbin@192.168.1.158:{0}' -f $RemoteFile
scp -o StrictHostKeyChecking=yes $Remote $File
if ($LASTEXITCODE -ne 0) { throw 'Backup download failed. Keep the old API stopped; do not activate.' }
$Hash = (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant()
if ($Hash -cne $ExpectedSha256 -or (Get-Item -LiteralPath $File).Length -ne $ExpectedBytes) {
    throw 'Backup hash/size mismatch. Do not activate.'
}
Write-Output "Protected off-server backup: $File"
Write-Output "SHA256: $Hash"
Write-Output "Bytes: $ExpectedBytes"
Write-Output 'Download verified. No original database restore, API startup or email occurred.'
