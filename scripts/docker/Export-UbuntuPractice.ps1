[CmdletBinding()]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (!$Apply) {
    Write-Output 'Preview only: -Apply builds three linux/amd64 images, exports a versioned bundle to ubuntu-practice-release/. No SSH, database access, secrets, container startup, commit or push.'
    return
}

function Invoke-Checked([string[]]$Arguments) {
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Docker command failed; stop. Existing release files were preserved.' }
}

Push-Location -LiteralPath $Root
try {
    Invoke-Checked @('version', '--format', '{{.Server.Version}}')
    $Tag = 'practice-' + [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ').ToLowerInvariant() + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $OutputRoot = Join-Path $Root 'ubuntu-practice-release'
    if (Test-Path -LiteralPath $OutputRoot) {
        $Item = Get-Item -LiteralPath $OutputRoot
        if (!$Item.PSIsContainer -or ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Release root must be an ordinary directory.'
        }
    }
    $Folder = Join-Path $OutputRoot $Tag
    if (Test-Path -LiteralPath $Folder) { throw 'Release already exists; never overwrite it.' }
    New-Item -ItemType Directory -Path $Folder | Out-Null

    # Build contexts are allowlists; no env files or SQL/Gmail secrets are build args.
    $Specs = @(
        @{Name='api'; Target='runtime'; File='backend/Project1.Api/Dockerfile'; Context='.'},
        @{Name='migrate'; Target='migrations'; File='backend/Project1.Api/Dockerfile'; Context='.'},
        @{Name='frontend'; Target='runtime'; File='frontend/Dockerfile'; Context='frontend'}
    )
    $Images = @{}
    foreach ($Spec in $Specs) {
        $Reference = 'project1-ubuntu-practice-' + $Spec.Name + ':' + $Tag
        Invoke-Checked @('build', '--pull', '--platform', 'linux/amd64', '--file', $Spec.File, '--target', $Spec.Target, '--tag', $Reference, $Spec.Context)
        # Select only metadata. Never dump container/image environment or full inspect.
        $Info = & docker image inspect --format '{{.Id}}|{{.Os}}|{{.Architecture}}' $Reference
        if ($LASTEXITCODE -ne 0 -or $Info -notmatch '^(sha256:[0-9a-f]{64})\|linux\|amd64$') {
            throw 'Unexpected image metadata.'
        }
        $Images[$Spec.Name] = @{reference=$Reference; id=$Matches[1]}
    }

    $Archive = Join-Path $Folder 'images.tar'
    Invoke-Checked @('image', 'save', '--output', $Archive, $Images.api.reference, $Images.migrate.reference, $Images.frontend.reference)
    $Hash = (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $MigrationIds = @(Get-ChildItem -LiteralPath 'backend/Project1.Api/Migrations' -File |
        Where-Object { $_.Name -cmatch '^\d{14}_[A-Za-z0-9_]+\.cs$' } |
        ForEach-Object { $_.BaseName } | Sort-Object)
    if ($MigrationIds.Count -eq 0) { throw 'No migrations found.' }
    $FileSources = @{
        'compose.ubuntu-practice.yml' = 'compose.ubuntu-practice.yml'
        '.env.docker.ubuntu-practice.example' = '.env.docker.ubuntu-practice.example'
        'ubuntu_practice.py' = 'scripts/docker/ubuntu_practice.py'
    }
    $FileHashes = @{}
    foreach ($Name in $FileSources.Keys) {
        Copy-Item -LiteralPath $FileSources[$Name] -Destination (Join-Path $Folder $Name)
        $FileHashes[$Name] = (Get-FileHash -LiteralPath (Join-Path $Folder $Name) -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $Manifest = @{format=1; tag=$Tag; images=$Images; archive_sha256=$Hash; migration_ids=$MigrationIds; files_sha256=$FileHashes}
    # Artifact metadata contains no credentials; no source tree/private configs are copied.
    [IO.File]::WriteAllText((Join-Path $Folder 'release.json'), ($Manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $Folder 'images.tar.sha256'), "$Hash  images.tar`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Release folder: $Folder"
    Write-Output "Tag: $Tag"
    Write-Output "Archive SHA256: $Hash"
    Write-Output 'Build/export only. Nothing was uploaded, started or migrated.'
} finally {
    Pop-Location
}
