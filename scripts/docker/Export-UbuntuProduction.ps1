[CmdletBinding()]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (!$Apply) {
    Write-Output 'Preview only: -Apply builds/exports TWO production linux/amd64 runtime images and public cutover files. No SSH, SQL, containerized application startup, commit or push.'
    return
}
function Invoke-Checked([string[]]$Arguments) {
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Docker command failed. Stop and preserve this release directory.' }
}

Push-Location -LiteralPath $Root
try {
    Invoke-Checked @('version', '--format', '{{.Server.Version}}')
    $Tag = 'prod-' + [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ').ToLowerInvariant() + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
    $OutputRoot = Join-Path $Root 'ubuntu-production-release'
    if (Test-Path -LiteralPath $OutputRoot) {
        $Item = Get-Item -LiteralPath $OutputRoot
        if (!$Item.PSIsContainer -or ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Release root must be an ordinary directory.'
        }
    }
    $Folder = Join-Path $OutputRoot $Tag
    if (Test-Path -LiteralPath $Folder) { throw 'Never overwrite an existing release.' }
    New-Item -ItemType Directory -Path $Folder | Out-Null
    $Images = @{}
    foreach ($Spec in @(
        @{Name='api'; File='backend/Project1.Api/Dockerfile'; Context='.'},
        @{Name='frontend'; File='frontend/Dockerfile'; Context='frontend'}
    )) {
        $Reference = 'project1-ubuntu-' + $Spec.Name + ':' + $Tag
        Invoke-Checked @('build', '--pull', '--platform', 'linux/amd64', '--target', 'runtime', '--file', $Spec.File, '--tag', $Reference, $Spec.Context)
        $Info = & docker image inspect --format '{{.Id}}|{{.Os}}|{{.Architecture}}|{{.Config.User}}' $Reference
        if ($LASTEXITCODE -ne 0 -or $Info -notmatch '^(sha256:[0-9a-f]{64})\|linux\|amd64\|([1-9][0-9]*)$') {
            throw 'Runtime image must be linux/amd64 with a non-root numeric user.'
        }
        $Images[$Spec.Name] = @{reference=$Reference; id=$Matches[1]; user=$Matches[2]}
    }
    # Read only a public static file hash inside an isolated, read-only ephemeral container.
    # No application, database, credentials, network or host mounts are involved.
    $Index = & docker run --rm --network none --read-only --entrypoint sha256sum $Images.frontend.reference /usr/share/nginx/html/index.html
    if ($LASTEXITCODE -ne 0 -or $Index -notmatch '^([0-9a-f]{64})\s+/usr/share/nginx/html/index.html$') {
        throw 'Could not verify frontend index hash.'
    }
    $IndexHash = $Matches[1]
    $Archive = Join-Path $Folder 'images.tar'
    Invoke-Checked @('image', 'save', '--output', $Archive, $Images.api.reference, $Images.frontend.reference)
    $Hash = (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $Ids = @(Get-ChildItem -LiteralPath 'backend/Project1.Api/Migrations' -File |
        Where-Object { $_.Name -cmatch '^\d{14}_[A-Za-z0-9_]+\.cs$' } | ForEach-Object { $_.BaseName } | Sort-Object)
    if (!$Ids.Count) { throw 'No migrations found; do not use an incomplete release.' }
    $Sources = @{
        'compose.ubuntu.yml'='compose.ubuntu.yml'
        '.env.docker.ubuntu.example'='.env.docker.ubuntu.example'
        'ubuntu_production.py'='scripts/docker/ubuntu_production.py'
        'project1-docker.conf'='scripts/docker/nginx/project1-docker.conf'
        'project1-legacy.conf'='scripts/docker/nginx/project1-legacy.conf'
    }
    $Hashes = @{}
    foreach ($Name in $Sources.Keys) {
        Copy-Item -LiteralPath $Sources[$Name] -Destination (Join-Path $Folder $Name)
        $Hashes[$Name] = (Get-FileHash -LiteralPath (Join-Path $Folder $Name) -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $Manifest = @{format=1; tag=$Tag; images=$Images; archive_sha256=$Hash; frontend_sha256=$IndexHash; migration_ids=$Ids; files_sha256=$Hashes}
    [IO.File]::WriteAllText((Join-Path $Folder 'release.json'), ($Manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $Folder 'images.tar.sha256'), "$Hash  images.tar`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Production release folder: $Folder"
    Write-Output "Tag: $Tag"
    Write-Output "Archive SHA256: $Hash"
    Write-Output 'Export complete. No SSH/SQL/cutover/email was performed.'
} finally { Pop-Location }
