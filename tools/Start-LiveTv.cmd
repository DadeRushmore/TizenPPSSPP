@echo off
cd /d "%~dp0.."
python tools\live-tv.py serve
pause
