@echo off
chcp 65001 >nul
title 鼠標工具發布編譯器

echo ========================================================
echo  正在終止可能佔用的舊進程...
echo ========================================================
taskkill /f /im 鼠標工具.exe >nul 2>&1
taskkill /f /im CursorTool.exe >nul 2>&1
taskkill /f /im HololiveCursorApp.exe >nul 2>&1
timeout /t 1 /nobreak >nul

echo.
echo ========================================================
echo  正在生成應用程式圖示 (app.ico)...
echo ========================================================
cd /d "%~dp0CursorApp"
if exist "convert_icon.py" (
    py convert_icon.py >nul 2>&1
)

echo.
echo ========================================================
echo  正在清理舊輸出目錄...
echo ========================================================
if exist "%~dp0鼠標工具.exe" del /f /q "%~dp0鼠標工具.exe"
if exist "%~dp0CursorTool.exe" del /f /q "%~dp0CursorTool.exe"
if exist "%~dp0*.pdb" del /f /q "%~dp0*.pdb"
if exist "bin" rd /s /q "bin" >nul 2>&1
if exist "obj" rd /s /q "obj" >nul 2>&1

echo.
echo ========================================================
echo  正在以標準 Release 模式發布單一獨立執行檔...
echo ========================================================
dotnet publish CursorApp.csproj -c Release -o "%~dp0PublishOut"

if exist "%~dp0PublishOut\CursorTool.exe" (
    copy /y "%~dp0PublishOut\CursorTool.exe" "%~dp0鼠標工具.exe" >nul
    move /y "%~dp0PublishOut\CursorTool.exe" "%~dp0CursorManager_v2.5.0.exe" >nul
    rd /s /q "%~dp0PublishOut" >nul 2>&1
    del /f /q "%~dp0*.pdb" >nul 2>&1
    echo.
    echo ========================================================
    echo  [發布成功] 已生成「CursorManager_v2.5.0.exe」與「鼠標工具.exe」！
    echo  現在您可以雙擊執行開啟使用。
    echo ========================================================
) else (
    echo.
    echo [發布失敗] 請查看上方的編譯輸出訊息。
)

echo.
pause
