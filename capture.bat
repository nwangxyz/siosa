@echo off
rem README screenshot mode: Siosa windows (overlay, reward highlight, tree) show up in PrintScreen / Win+Shift+S.
rem Quit the running Siosa from the tray first.
start "" "%~dp0Overlay\bin\out\Siosa.exe" --showcapture
