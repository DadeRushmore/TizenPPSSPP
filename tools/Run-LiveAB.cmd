@echo off
cd /d "%~dp0.."
python tools\live-tv.py command ab --seconds 30 --order ABBA
if errorlevel 1 goto finish
echo This only queues the request. Watch the Start-LiveTv window for the TV reply.
echo OK capture_started confirms acceptance; AB_DONE marks completion.
:finish
pause
