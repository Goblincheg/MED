@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Build-MEDRelease.ps1" %*
exit /b %ERRORLEVEL%
