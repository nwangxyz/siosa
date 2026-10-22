# Siosa 포터블 배포 파일 만들기
#   powershell -ExecutionPolicy Bypass -File release.ps1      (또는 release.bat)
# 결과: dist\Siosa-v<버전>.zip
#   Siosa\Siosa.exe      프로그램 하나 (게임 데이터·아이콘·QR 모두 exe 안)
#   Siosa\README.txt     사용자 안내 (docs\README.txt)
# 설정·프리셋·진행 기록은 처음 실행할 때 exe 옆 SiosaData\ 에 생긴다 → exe 와 SiosaData 를 같이 옮기면 그대로.
# 버전은 Overlay\AppInfo.cs 의 Version. GitHub 릴리스 태그는 v<버전>, 첨부 파일 이름은 그대로 Siosa-v<버전>.zip.
# (이 파일은 UTF-8 BOM 으로 저장: Windows PowerShell 5.1 은 BOM 없는 스크립트를 ANSI 로 읽어 한글에서 깨진다)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$appInfo = Get-Content (Join-Path $root 'Overlay\AppInfo.cs') -Raw -Encoding UTF8
if ($appInfo -notmatch 'const string Version = "([0-9]+\.[0-9]+\.[0-9]+)"') { throw 'AppInfo.cs 에서 버전을 찾지 못했습니다' }
$ver = $Matches[1]
Write-Host "Siosa v$ver"

$dist = Join-Path $root 'dist'
$build = Join-Path $dist 'build'
$pkg = Join-Path $dist 'Siosa'
foreach ($d in @($build, $pkg)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }   # dist 안의 이전 결과만 지운다
New-Item -ItemType Directory -Force $pkg | Out-Null

& (Join-Path $root 'tools\dotnet\dotnet.exe') build (Join-Path $root 'Overlay') -c Release -o $build
if ($LASTEXITCODE -ne 0) { throw '빌드 실패' }

Copy-Item (Join-Path $build 'Siosa.exe') $pkg
Copy-Item (Join-Path $root 'docs\README.txt') (Join-Path $pkg 'README.txt')

$zip = Join-Path $dist "Siosa-v$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $pkg -DestinationPath $zip
Remove-Item $build -Recurse -Force
Write-Host "완료: $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB)"
