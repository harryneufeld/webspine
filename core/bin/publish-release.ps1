[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$AssetsDirectory,
    [string]$Commit,
    [string]$NotesPath,
    [string]$Php='php',
    [switch]$VerifyOnly
)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path (Split-Path $PSScriptRoot)
$verification=& $Php (Join-Path $PSScriptRoot 'release.php') verify-set $AssetsDirectory $Version
if($LASTEXITCODE -ne 0){throw 'Release asset verification failed; nothing uploaded.'}
$assets=@(($verification -join "`n") | ConvertFrom-Json)
if($assets.Count -ne 4){throw 'Expected exactly four verified assets.'}
if($VerifyOnly){$assets | ConvertTo-Json;exit 0}
if($Commit -notmatch '^[a-f0-9]{40}$' -or !$NotesPath){throw 'Specify the merged commit and release notes file.'}
$head=git -C $repoRoot rev-parse HEAD
if($LASTEXITCODE -ne 0 -or $head -ne $Commit){throw 'Publisher checkout must match the merged commit.'}
$dirty=git -C $repoRoot status --porcelain --untracked-files=no
if($LASTEXITCODE -ne 0 -or $dirty){throw 'Publisher requires a clean tracked checkout.'}
$versionText=git -C $repoRoot show "${Commit}:core/version.php"
if($LASTEXITCODE -ne 0){throw 'Cannot read committed release version.'}
$versionMatches=[regex]::Matches(($versionText -join "`n"), "'version'\s*=>\s*'([^']*)'")
if($versionMatches.Count -ne 1 -or $versionMatches[0].Groups[1].Value -ne $Version){throw 'Committed version differs from requested release.'}
$inventoryPaths=@('core','public','site','README.md','LICENSE','AGENTS.md','config/example.php','storage/.gitkeep','.gitignore','.gitattributes')
$untracked=git -C $repoRoot ls-files --others --exclude-standard -- @inventoryPaths
if($LASTEXITCODE -ne 0 -or $untracked){throw 'Untracked files in release inventory paths.'}
$ignored=git -C $repoRoot ls-files --others --ignored --exclude-standard -- @inventoryPaths
if($LASTEXITCODE -ne 0 -or $ignored){throw 'Ignored files in release inventory paths.'}
# Compare each archive inventory with the trusted checkout, not only its sidecar.
$sourceVerification=& $Php (Join-Path $PSScriptRoot 'release.php') verify-set $AssetsDirectory $Version $repoRoot
if($LASTEXITCODE -ne 0){throw 'Release source verification failed.'}
$assets=@(($sourceVerification -join "`n") | ConvertFrom-Json)
if(!$env:GITHUB_TOKEN){throw 'Set GITHUB_TOKEN with release permission for the official repository.'}
$notes=Get-Content -LiteralPath $NotesPath -Raw -Encoding UTF8
if([string]::IsNullOrWhiteSpace($notes)){throw 'Release notes are empty.'}
$headers=@{Authorization='Bearer '+$env:GITHUB_TOKEN;Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28'}
$repo='https://api.github.com/repos/harryneufeld/webspine'
$main=Invoke-RestMethod "$repo/commits/main" -Headers $headers
if($main.sha -ne $Commit){throw 'Only the current merged main commit may be published.'}
$checks=Invoke-RestMethod "$repo/commits/$Commit/check-runs?per_page=100" -Headers $headers
$hosting=@($checks.check_runs | Where-Object name -eq 'servers')
if($checks.total_count -gt 100 -or $hosting.Count -lt 1 -or @($hosting | Where-Object conclusion -ne 'success').Count -gt 0 -or
    @($checks.check_runs | Where-Object {$_.status -ne 'completed' -or $_.conclusion -notin @('success','skipped','neutral')}).Count -gt 0){throw 'Merged commit CI is incomplete or failed.'}
# A pre-existing tag/draft is left for operator review, never overwritten.
$releases=Invoke-RestMethod "$repo/releases?per_page=100" -Headers $headers
if($releases.tag_name -contains "v$Version"){throw 'Release or draft already exists.'}
$tagExists=$false
try {$null=Invoke-RestMethod "$repo/git/ref/tags/v$Version" -Headers $headers;$tagExists=$true}
catch {if([int]$_.Exception.Response.StatusCode -ne 404){throw}}
if($tagExists){throw 'Release tag already exists.'}
$body=@{tag_name="v$Version";target_commitish=$Commit;name="webspine $Version";body=$notes;draft=$true;prerelease=$false;make_latest='true'}|ConvertTo-Json
$release=Invoke-RestMethod "$repo/releases" -Headers $headers -Method Post -ContentType 'application/json' -Body $body
Write-Output "Draft release created: $($release.id)"
$upload=$release.upload_url -replace '\{.*$',''
foreach($asset in $assets){
    $current=Get-Item -LiteralPath $asset.path
    if($current.Length -ne $asset.size -or (Get-FileHash -LiteralPath $asset.path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asset.sha256){throw 'Asset changed before upload; draft remains unpublished.'}
    $uploaded=Invoke-RestMethod ($upload+'?name='+[uri]::EscapeDataString($asset.name)) -Headers $headers -Method Post -ContentType 'application/octet-stream' -InFile $asset.path
    if($uploaded.state -ne 'uploaded' -or $uploaded.size -ne $asset.size -or $uploaded.digest -ne ('sha256:'+$asset.sha256)){throw 'Uploaded asset verification failed; draft remains unpublished.'}
}
$ready=Invoke-RestMethod "$repo/releases/$($release.id)" -Headers $headers
if($ready.assets.Count -ne 4 -or @($ready.assets | Where-Object {$_.name -notin $assets.name}).Count -gt 0){throw 'Unexpected draft assets; nothing published.'}
$published=Invoke-RestMethod "$repo/releases/$($release.id)" -Headers $headers -Method Patch -ContentType 'application/json' -Body (@{draft=$false;prerelease=$false;make_latest='true'}|ConvertTo-Json)
$latest=Invoke-RestMethod "$repo/releases/latest" -Headers $headers
$tag=Invoke-RestMethod "$repo/git/ref/tags/v$Version" -Headers $headers
if($latest.tag_name -ne "v$Version" -or $tag.object.sha -ne $Commit){throw 'Published release target verification failed.'}
Write-Output $published.html_url
