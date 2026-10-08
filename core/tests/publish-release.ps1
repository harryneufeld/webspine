param([string]$Php='php')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path (Split-Path $PSScriptRoot)
$fixture=Join-Path $repoRoot ('storage/publisher-test-'+[guid]::NewGuid().ToString('N'))
$priorToken=$env:GITHUB_TOKEN
$priorFixtureState=Get-Variable -Name publisherFixtureState -Scope Global -ErrorAction SilentlyContinue
$global:publisherFixtureState=@{commit='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';fault='';uploaded=@();published=$false;draft=$false;version=''}
class PublisherFixtureException : System.Exception {
    [object]$Response
    PublisherFixtureException() : base('Fixture 404') { $this.Response=@{StatusCode=404} }
}
# Only Git state and HTTP are simulated; the actual builder/verifier/publisher run.
function git {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
    $global:LASTEXITCODE=0
    switch($Arguments[2]) {
        'rev-parse' {$global:publisherFixtureState.commit}
        'show' {Get-Content -LiteralPath (Join-Path $repoRoot 'core/version.php')}
        'status' {}
        'ls-files' {}
        default {throw 'Unexpected fixture Git command'}
    }
}
function Invoke-RestMethod {
    param([string]$Uri,[object]$Headers,[string]$Method='Get',[string]$ContentType,[string]$Body,[string]$InFile)
    if($Uri.EndsWith('/commits/main')){return @{sha=$global:publisherFixtureState.commit}}
    if($Uri.Contains('/check-runs')){return @{total_count=1;check_runs=@(@{name='servers';status='completed';conclusion=$(if($global:publisherFixtureState.fault -eq 'ci'){'failure'}else{'success'})})}}
    if($Uri.Contains('/git/ref/tags/')){
        if($global:publisherFixtureState.published -or $global:publisherFixtureState.fault -eq 'tag'){return @{object=@{sha=$global:publisherFixtureState.commit}}}
        throw [PublisherFixtureException]::new()
    }
    if($Uri.EndsWith('/releases?per_page=100')){return @()}
    if($Uri.EndsWith('/releases/latest')){return @{tag_name="v$($global:publisherFixtureState.version)"}}
    if($Uri.EndsWith('/releases') -and $Method -eq 'Post'){
        $global:publisherFixtureState.draft=$true;return @{id=123;upload_url='https://fixture.invalid/upload{?name}'}
    }
    if($Uri.StartsWith('https://fixture.invalid/upload?name=')){
        $file=Get-Item -LiteralPath $InFile
        $name=[uri]::UnescapeDataString(($Uri -split '\?name=',2)[1])
        if($name -ne $file.Name){throw 'Upload filename mismatch'}
        $hash=(Get-FileHash -LiteralPath $InFile -Algorithm SHA256).Hash.ToLowerInvariant()
        $asset=@{name=$name;size=$file.Length;state='uploaded';digest=$(if($global:publisherFixtureState.fault -eq 'digest'){'sha256:bad'}else{'sha256:'+$hash})}
        $global:publisherFixtureState.uploaded+=,$asset;return $asset
    }
    if($Uri.EndsWith('/releases/123') -and $Method -eq 'Get'){return @{assets=$global:publisherFixtureState.uploaded}}
    if($Uri.EndsWith('/releases/123') -and $Method -eq 'Patch'){
        if($global:publisherFixtureState.uploaded.Count -ne 4){throw 'Premature publication'}
        $global:publisherFixtureState.published=$true;return @{html_url='https://fixture.invalid/released'}
    }
    throw ('Unexpected fixture HTTP request: '+$Uri)
}
try {
    $build=& $Php (Join-Path $repoRoot 'core/bin/release.php') build $repoRoot $fixture
    if($LASTEXITCODE -ne 0){throw 'Fixture build failed'}
    $assets=@(($build -join "`n")|ConvertFrom-Json)
    $global:publisherFixtureState.version=($assets[0].name -replace '^webspine-core-','') -replace '\.zip$',''
    Set-Content -LiteralPath (Join-Path $fixture 'webspine-0.1.7.zip') -Value 'Unrelated old artifact'
    $notes=Join-Path $fixture 'notes.md';Set-Content -LiteralPath $notes -Value 'Fixture release notes'
    $env:GITHUB_TOKEN='offline-fixture-token'
    foreach($case in @('','digest','ci','tag')){
        $global:publisherFixtureState.fault=$case;$global:publisherFixtureState.uploaded=@();$global:publisherFixtureState.published=$false;$global:publisherFixtureState.draft=$false;$failure=$null
        try {$null=& (Join-Path $repoRoot 'core/bin/publish-release.ps1') -Version $global:publisherFixtureState.version -AssetsDirectory $fixture -Commit $global:publisherFixtureState.commit -NotesPath $notes -Php $Php}
        catch {$failure=$_}
        if($case -eq ''){
            if($failure){throw $failure}
            if($failure -or !$global:publisherFixtureState.published -or $global:publisherFixtureState.uploaded.Count -ne 4 -or @($global:publisherFixtureState.uploaded|Where-Object {$_.name -notin $assets.name}).Count){throw 'Exact successful publication failed'}
        }elseif(!$failure -or $global:publisherFixtureState.published){throw 'Failed gate published a release'}
        if($case -in @('ci','tag') -and $global:publisherFixtureState.draft){throw 'Failed preflight created a draft'}
        if($case -eq 'digest' -and (!$global:publisherFixtureState.draft -or $global:publisherFixtureState.uploaded.Count -ne 1)){throw 'Upload failure did not stop with unpublished draft'}
    }
    Write-Output 'Publisher tests passed: four exact assets, digest failure, failed CI, existing tag; no network.'
}finally {
    $env:GITHUB_TOKEN=$priorToken
    if($priorFixtureState){Set-Variable -Name publisherFixtureState -Scope Global -Value $priorFixtureState.Value}
    else {Remove-Variable -Name publisherFixtureState -Scope Global -ErrorAction SilentlyContinue}
    if(Test-Path -LiteralPath $fixture){
        $resolved=(Resolve-Path -LiteralPath $fixture).Path
        $allowed=(Resolve-Path -LiteralPath (Join-Path $repoRoot 'storage')).Path+[IO.Path]::DirectorySeparatorChar
        if(!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe fixture cleanup'}
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
