@echo off
chcp 6501 >nul
title Установка приложения «Таймер»
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 pause
