@echo off
cd /d "%~dp0"
python psx_pack_prepare.py %*
if errorlevel 1 pause
