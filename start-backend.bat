@echo off
setlocal
cd /d "%~dp0"

if /I "%~1"=="--backend" goto backend
if /I "%~1"=="--frontend" goto frontend

title Restaurant Food Manager - Launcher
call :prerequisites
if errorlevel 1 (
    pause
    exit /b 1
)

if /I "%~1"=="--check" (
    echo OK: da tim thay backend, frontend, dotnet va Python 3.
    echo Backend: http://localhost:5225 - Development
    echo Frontend: http://localhost:5500/pages/login.html
    echo MigrateOnStartup=false, SeedOnStartup=false
    exit /b 0
)

call :portInUse 5225
if errorlevel 10 (
    echo Cong 5225 dang duoc su dung, bo qua khoi dong backend.
) else (
    start "Restaurant Food Manager - Backend" "%ComSpec%" /d /c call "%~f0" --backend
)

call :portInUse 5500
if errorlevel 10 (
    echo Cong 5500 dang duoc su dung, bo qua khoi dong frontend.
) else (
    start "Restaurant Food Manager - Frontend" "%ComSpec%" /d /c call "%~f0" --frontend
)

echo.
echo Da gui lenh khoi dong. Cho backend build xong trong cua so Backend.
echo Mo website: http://localhost:5500/pages/login.html
echo Swagger: http://localhost:5225/swagger/index.html
echo Neu co loi, xem thong bao trong tung cua so.
echo Nhan Ctrl+C trong ca hai cua so Backend va Frontend de dung ung dung.
pause
exit /b 0

:backend
title Restaurant Food Manager - Backend
call :portInUse 5225
if errorlevel 10 (
    echo Cong 5225 dang duoc su dung. Khong khoi dong them backend.
    pause
    exit /b 0
)
echo Dang khoi dong backend tai http://localhost:5225
echo Dung database dang cau hinh; khong tu migrate hoac nap du lieu mau.
echo Nhan Ctrl+C de dung backend.
echo.
dotnet run --project "RestaurantFoodManagerSystem\RestaurantFoodManagerSystem.csproj" --launch-profile http -- --Database:MigrateOnStartup=false --Database:SeedOnStartup=false
set "taskServerExit=%ERRORLEVEL%"
goto stopped

:frontend
title Restaurant Food Manager - Frontend
call :findPython
if errorlevel 1 (
    pause
    exit /b 1
)
call :portInUse 5500
if errorlevel 10 (
    echo Cong 5500 dang duoc su dung. Khong khoi dong them frontend.
    pause
    exit /b 0
)
echo Dang khoi dong frontend tai http://localhost:5500/pages/login.html
echo Nhan Ctrl+C de dung frontend.
echo.
%taskPythonCommand% -m http.server 5500 --bind localhost --directory "%~dp0frontend"
set "taskServerExit=%ERRORLEVEL%"

:stopped
echo.
echo Tien trinh da dung. Ma thoat: %taskServerExit%
pause
exit /b %taskServerExit%

:prerequisites
if not exist "RestaurantFoodManagerSystem\RestaurantFoodManagerSystem.csproj" (
    echo Khong tim thay project backend. Hay giu file BAT trong thu muc goc repository.
    exit /b 1
)

if not exist "frontend\pages\login.html" (
    echo Khong tim thay frontend. Hay giu file BAT trong thu muc goc repository.
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Khong tim thay dotnet. Can cai .NET SDK 8 va mo lai file nay.
    exit /b 1
)

call :findPython
exit /b %ERRORLEVEL%

:findPython
set "taskPythonCommand="
py -3 -c "import sys; sys.exit(sys.version_info.major != 3)" >nul 2>nul
if not errorlevel 1 (
    set "taskPythonCommand=py -3"
    exit /b 0
)

python -c "import sys; sys.exit(sys.version_info.major != 3)" >nul 2>nul
if not errorlevel 1 (
    set "taskPythonCommand=python"
    exit /b 0
)

echo Khong tim thay Python 3. Can cai Python 3 va mo lai file nay.
exit /b 1

:portInUse
powershell.exe -NoProfile -Command "if (Get-NetTCPConnection -State Listen -LocalPort %~1 -ErrorAction SilentlyContinue) { exit 10 }; exit 0"
exit /b %ERRORLEVEL%
