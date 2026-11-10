@echo off
setlocal
title Restaurant Food Manager - Backend
cd /d "%~dp0"

if not exist "RestaurantFoodManagerSystem\RestaurantFoodManagerSystem.csproj" (
    echo Khong tim thay project backend. Hay giu file BAT trong thu muc goc repository.
    pause
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Khong tim thay dotnet. Can cai .NET SDK 8 va mo lai file nay.
    pause
    exit /b 1
)

if /I "%~1"=="--check" (
    echo OK: da tim thay project backend va dotnet.
    echo Profile: http - Development - localhost:5225
    echo MigrateOnStartup=false, SeedOnStartup=false
    exit /b 0
)

powershell.exe -NoProfile -Command "if (Get-NetTCPConnection -State Listen -LocalPort 5225 -ErrorAction SilentlyContinue) { exit 10 }"
if errorlevel 10 (
    echo Cong 5225 dang duoc su dung. Khong khoi dong them backend.
    echo Neu backend da chay, mo http://localhost:5225/swagger/index.html
    pause
    exit /b 0
)

echo Dang khoi dong backend tai http://localhost:5225
echo Dung database dang cau hinh; khong tu migrate hoac nap du lieu mau.
echo Nhan Ctrl+C de dung backend.
echo.
dotnet run --project "RestaurantFoodManagerSystem\RestaurantFoodManagerSystem.csproj" --launch-profile http -- --Database:MigrateOnStartup=false --Database:SeedOnStartup=false
set "taskBackendExit=%ERRORLEVEL%"
echo.
echo Backend da dung. Ma thoat: %taskBackendExit%
pause
exit /b %taskBackendExit%
