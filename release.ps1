# Siosa 포터블 배포 파일 만들기
#   powershell -ExecutionPolicy Bypass -File release.ps1      (또는 release.bat)
#   GitHub Actions(.github\workflows\release.yml)는 태그를 넘겨 부른다: release.ps1 -Tag v1.0.0
# 결과: dist\Siosa-v<버전>.zip
#   Siosa\Siosa.exe      프로그램 하나 (게임 데이터·아이콘·QR 모두 exe 안)
#   Siosa\README.txt     사용자 안내 (docs\README.txt)
# 설정·프리셋·진행 기록은 처음 실행할 때 exe 옆 SiosaData\ 에 생긴다 → exe 와 SiosaData 를 같이 옮기면 그대로.
# 버전은 Overlay\AppInfo.cs 의 Version. GitHub 릴리스 태그는 v<버전>, 첨부 파일 이름은 그대로 Siosa-v<버전>.zip.
# (이 파일은 UTF-8 BOM 으로 저장: Windows PowerShell 5.1 은 BOM 없는 스크립트를 ANSI 로 읽어 한글에서 깨진다)

param([string]$Tag = '')   # 주면 AppInfo.cs 버전과 같은지 확인 (예: v1.0.0)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$appInfo = Get-Content (Join-Path $root 'Overlay\AppInfo.cs') -Raw -Encoding UTF8
if ($appInfo -notmatch 'const string Version = "([0-9]+\.[0-9]+\.[0-9]+)"') { throw 'AppInfo.cs 에서 버전을 찾지 못했습니다' }
$ver = $Matches[1]
Write-Host "Siosa v$ver"
if ($Tag -and $Tag -ne "v$ver") { throw "태그($Tag)와 AppInfo.cs 버전(v$ver)이 다릅니다. AppInfo.cs 의 Version 을 고치고 다시 태그를 붙이세요." }

$dist = Join-Path $root 'dist'
$build = Join-Path $dist 'build'
$pkg = Join-Path $dist 'Siosa'
foreach ($d in @($build, $pkg)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }   # dist 안의 이전 결과만 지운다
New-Item -ItemType Directory -Force $pkg | Out-Null

# 이 PC: 저장소에 받아 둔 tools\dotnet. GitHub Actions(Windows 서버): 설치된 dotnet
$dotnet = Join-Path $root 'tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet build (Join-Path $root 'Overlay') -c Release -o $build
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }

Copy-Item (Join-Path $build 'Siosa.exe') $pkg
Copy-Item (Join-Path $root 'docs\README.txt') (Join-Path $pkg 'README.txt')

# 릴리스 페이지 본문 (사용자용): 이번 버전 변경 사항(docs\CHANGELOG.md 의 '## v<버전>') + 공통 안내(docs\release-notes.md)
#   앱의 업데이트 확인 창에는 본문 앞부분이 보이므로 변경 사항을 맨 위에 둔다
$changelog = Get-Content (Join-Path $root 'docs\CHANGELOG.md') -Raw -Encoding UTF8
$m = [regex]::Match($changelog, "(?ms)^## v$([regex]::Escape($ver))\s*\r?\n(.*?)(?=^## |\z)")
if (-not $m.Success) { throw "docs\CHANGELOG.md 에 '## v$ver' 항목이 없습니다. 이번 버전 변경 사항을 적어 주세요." }
$guide = (Get-Content (Join-Path $root 'docs\release-notes.md') -Raw -Encoding UTF8).Replace('{VERSION}', $ver)
#   GitHub 는 첨부 파일(Assets) 목록을 본문 아래에만 보여 주므로, 바로 받는 링크를 본문 맨 위에 둔다
$download = "https://github.com/nwangxyz/siosa/releases/download/v$ver/Siosa-v$ver.zip"
$notes = "### [⬇ Siosa-v$ver.zip 받기]($download)`n`n" + "## v$ver 변경 사항`n`n" + $m.Groups[1].Value.Trim() + "`n`n" + $guide
[IO.File]::WriteAllText((Join-Path $dist 'release-notes.md'), $notes, (New-Object Text.UTF8Encoding($false)))

$zip = Join-Path $dist "Siosa-v$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $pkg -DestinationPath $zip
Remove-Item $build -Recurse -Force
Write-Host "완료: $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB)"
