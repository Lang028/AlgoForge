@echo off
REM Starts the person pipeline service. Keep this window OPEN while you work --
REM closing it stops the service, and uploads then save photos with nobody detected.
cd /d "%~dp0"
echo Starting the person pipeline service on http://127.0.0.1:8000
echo Leave this window open. Press Ctrl+C to stop.
echo.
".venv\Scripts\python.exe" -m uvicorn main:app --host 127.0.0.1 --port 8000
pause
