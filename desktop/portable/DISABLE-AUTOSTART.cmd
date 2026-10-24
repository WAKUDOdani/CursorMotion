@echo off
"%~dp0CursorMotion.exe" --disable-autostart
if errorlevel 1 (echo Autostart failed.) else (echo Autostart disabled.)
pause
