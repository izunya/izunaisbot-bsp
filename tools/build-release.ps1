<#
    izunaisbot-bsp 릴리스 빌드 스크립트.

    3개 Beat Saber 버전(1.29.1 / 1.40.8 / 1.44.0)을 각각 Release 로 빌드하고
    dist\izunaisbot-bsp-<Version>-bs<게임버전>.zip 으로 패키징한다.
    각 zip 은 압축 해제 시 곧바로 Plugins\izunaisbot-bsp.dll 구조가 되도록 담는다.

    사용 예:
      # 빌드 + 패키징만 (dist\ 에 zip 3개 생성)
      pwsh tools\build-release.ps1

      # 버전 지정
      pwsh tools\build-release.ps1 -Version 0.6.1

      # 빌드 + 패키징 + GitHub 릴리스 생성/업로드 (태그 v<Version>)
      pwsh tools\build-release.ps1 -Version 0.6.1 -Publish

    -Publish 는 gh CLI 로그인 상태를 요구한다. 이미 같은 태그의 릴리스가 있으면
    자산만 덮어쓴다(--clobber).
#>
[CmdletBinding()]
param(
    # 릴리스 버전. 생략 시 메인 csproj 의 <Version> 값을 사용.
    [string]$Version,
    # 지정 시 gh 로 GitHub 릴리스를 만들고 zip 3개를 업로드.
    [switch]$Publish,
    # -Publish 시 릴리스 노트로 쓸 마크다운 파일 (없으면 자동 생성 노트).
    [string]$NotesFile,
    # -Publish 시 태그가 가리킬 대상. 기본은 현재 HEAD 커밋(SHA).
    # main 이 아닌 브랜치에서 릴리스할 때 태그가 엉뚱한 커밋을 가리키지 않도록 명시.
    [string]$Target
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# ---- 빌드 타깃 정의 ----
# gv  : 게임 버전(자산 파일명 접미사 bs<gv>)
# proj: 빌드할 csproj (repo 루트 기준 상대경로)
# 각 csproj 는 AppendTargetFrameworkToOutputPath=false → 산출물은 <projDir>\bin\Release\izunaisbot-bsp.dll
$targets = @(
    [pscustomobject]@{ gv = '1.29.1'; proj = '1.29.1\izunaisbot-bsp-1.29.1.csproj' }
    [pscustomobject]@{ gv = '1.40.8'; proj = 'izunaisbot-bsp.csproj' }
    [pscustomobject]@{ gv = '1.44.0'; proj = '1.44.0\izunaisbot-bsp-1.44.0.csproj' }
)

$asmName = 'izunaisbot-bsp'

# ---- 버전 확정 ----
if ([string]::IsNullOrWhiteSpace($Version)) {
    $mainProj = Join-Path $repoRoot 'izunaisbot-bsp.csproj'
    $xml = [xml](Get-Content $mainProj)
    $Version = ($xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($Version)) { throw "메인 csproj 에서 <Version> 을 찾지 못했습니다. -Version 으로 지정하세요." }
    Write-Host "Version(메인 csproj): $Version" -ForegroundColor Cyan
} else {
    Write-Host "Version: $Version" -ForegroundColor Cyan
}

$distDir = Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$artifacts = @()
foreach ($t in $targets) {
    Write-Host "`n=== Build $($t.gv) : $($t.proj) ===" -ForegroundColor Yellow
    $projPath = Join-Path $repoRoot $t.proj

    dotnet build $projPath -c Release -p:Version=$Version -nologo -v m
    if ($LASTEXITCODE -ne 0) { throw "빌드 실패: $($t.proj) (게임 참조 경로/BeatSaberDir 확인)" }

    $projDir = Split-Path -Parent $projPath
    $dll = Join-Path $projDir "bin\Release\$asmName.dll"
    if (-not (Test-Path $dll)) { throw "산출물 없음: $dll" }

    # 패키징: 임시 스테이징 폴더에 Plugins\<dll> 배치 후 zip
    $stage = Join-Path $distDir "_pkg-$($t.gv)"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Join-Path $stage 'Plugins') | Out-Null
    Copy-Item $dll (Join-Path $stage 'Plugins')

    $zip = Join-Path $distDir "$asmName-$Version-bs$($t.gv).zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    Remove-Item $stage -Recurse -Force

    $size = [math]::Round((Get-Item $zip).Length / 1KB, 1)
    Write-Host "  → $zip (${size} KB)" -ForegroundColor Green
    $artifacts += $zip
}

Write-Host "`n패키징 완료: $($artifacts.Count) 개" -ForegroundColor Cyan
$artifacts | ForEach-Object { Write-Host "  $_" }

# ---- GitHub 릴리스 (선택) ----
if ($Publish) {
    $tag = "v$Version"
    Write-Host "`n=== GitHub 릴리스: $tag ===" -ForegroundColor Yellow

    $notesArgs = @()
    if ($NotesFile -and (Test-Path $NotesFile)) {
        $notesArgs = @('--notes-file', $NotesFile)
    } else {
        $notesArgs = @('--generate-notes')
    }

    # 태그가 가리킬 커밋: -Target 없으면 현재 HEAD SHA (main 아닌 브랜치 대비).
    if ([string]::IsNullOrWhiteSpace($Target)) { $Target = (git rev-parse HEAD).Trim() }
    Write-Host "태그 대상 커밋: $Target" -ForegroundColor Cyan

    # 릴리스가 이미 있으면 자산만 덮어쓰기, 없으면 새로 생성.
    gh release view $tag *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "릴리스 $tag 존재 → 자산 업로드(--clobber)" -ForegroundColor Cyan
        gh release upload $tag @artifacts --clobber
    } else {
        Write-Host "릴리스 $tag 생성" -ForegroundColor Cyan
        gh release create $tag @artifacts --title $Version --target $Target @notesArgs
    }
    if ($LASTEXITCODE -ne 0) { throw "gh 릴리스 실패" }
    Write-Host "릴리스 완료: $tag" -ForegroundColor Green
}
