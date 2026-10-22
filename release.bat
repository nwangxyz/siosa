@echo off
rem Siosa 포터블 배포 파일 만들기 → dist\Siosa-v<버전>.zip  (자세한 내용은 release.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1"
