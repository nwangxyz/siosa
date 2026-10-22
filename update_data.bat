@echo off
rem Run after a game patch: re-extract tables from Content.ggpk and rebuild data*.json
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "GGPK=C:\Kakaogames\Path of Exile\Content.ggpk"
if not "%~1"=="" set "GGPK=%~1"

set TABLES=worldareas quest questrewards questrewardoffers characters skillgems baseitemtypes itemclasses npcs npctalk itemvisualidentity queststaticrewards
set FILES=
for %%t in (%TABLES%) do set "FILES=!FILES! data/%%t.datc64 data/korean/%%t.datc64"
rem Mods (English only): implicit mods tell apart rewards with the same name (Two-Stone Ring x3)
set "FILES=!FILES! data/mods.datc64"

if not exist Extractor\bin\out\Siosa.Extractor.exe (
  tools\dotnet\dotnet.exe build Extractor -c Release -o Extractor\bin\out || exit /b 1
)
Extractor\bin\out\Siosa.Extractor.exe dump "%GGPK%" tools\raw %FILES% || exit /b 1
rem Quest vendor gem lists are not in the GGPK: download them from poewiki.net (needs internet)
python -I Extractor\fetch_vendor.py tools\raw\vendor_wiki.json || exit /b 1
python -I Extractor\build_data.py tools\raw tools\schema\schema-poe1.min.json data || exit /b 1
rem Passive tree layout (Kakao official tree page, Korean, needs internet): only positions and mastery texts are kept in data\tree.json
if not exist tools\raw\tree mkdir tools\raw\tree
python -I Extractor\fetch_tree_ko.py tools\raw\tree\data_ko.json || exit /b 1
python -I Extractor\build_tree.py tools\raw\tree\data_ko.json data || exit /b 1
python -I Extractor\build_icons.py tools\raw tools\schema\schema-poe1.min.json data Extractor\bin\out\Siosa.Extractor.exe "%GGPK%" || exit /b 1
echo Done.
