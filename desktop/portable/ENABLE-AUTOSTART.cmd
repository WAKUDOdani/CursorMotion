@echo off
"%~dp0CursorMotion.exe" --enable-autostart
if errorlevel 1 (echo Autostart failed.) else (echo Autostart enabled for this folder.)
pause
