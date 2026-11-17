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
    echo MigrateOnStartup=true, SeedOnStartup=true - BasicDataSeeder
    echo JWT: dung bien moi truong hoac khoa rieng duoc luu tren may.
    echo Check khong chay backend, migration hoac seed.
    exit /b 0
)

call :portInUse 5225
if errorlevel 10 (
    echo Cong 5225 dang duoc su dung, bo qua khoi dong backend.
    echo Muon chay lai seed: dung backend cu roi mo lai file BAT.
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
call :backendPrerequisites
if errorlevel 1 goto failed
call :portInUse 5225
if errorlevel 10 (
    echo Cong 5225 dang duoc su dung. Khong khoi dong them backend.
    pause
    exit /b 0
)
echo Dang khoi dong backend tai http://localhost:5225
echo Dung database dang cau hinh; ap dung migration va BasicDataSeeder.
echo Seed bo sung du lieu con thieu; khong dat lai mat khau tai khoan da co.
echo Nhan Ctrl+C de dung backend.
echo.
set "ASPNETCORE_ENVIRONMENT=Development"
set "DOTNET_ENVIRONMENT=Development"
set "taskProject=%~dp0RestaurantFoodManagerSystem\RestaurantFoodManagerSystem.csproj"
pushd "%~dp0RestaurantFoodManagerSystem"
powershell.exe -NoProfile -Command ^
    "$ErrorActionPreference = 'Stop'; try {" ^
    "if ([string]::IsNullOrWhiteSpace($env:Authentication__Jwt__Key)) {" ^
    "$taskKeyDirectory = Join-Path $env:LOCALAPPDATA 'RestaurantFoodManagerSystem';" ^
    "$taskKeyPath = Join-Path $taskKeyDirectory 'jwt-signing-key.txt';" ^
    "[void][IO.Directory]::CreateDirectory($taskKeyDirectory);" ^
    "if (-not [IO.File]::Exists($taskKeyPath)) {" ^
    "$taskKeyBytes = New-Object byte[] 48; $taskRandom = [Security.Cryptography.RandomNumberGenerator]::Create();" ^
    "try { $taskRandom.GetBytes($taskKeyBytes) } finally { $taskRandom.Dispose() };" ^
    "[IO.File]::WriteAllText($taskKeyPath, [Convert]::ToBase64String($taskKeyBytes));" ^
    "Write-Host 'Da tao khoa JWT rieng tren may.';" ^
    "}; $env:Authentication__Jwt__Key = [IO.File]::ReadAllText($taskKeyPath).Trim();" ^
    "}; if ([Text.Encoding]::UTF8.GetByteCount($env:Authentication__Jwt__Key) -lt 32) { throw 'Khoa JWT phai dai it nhat 32 byte. Kiem tra bien moi truong hoac file khoa rieng.' };" ^
    "& dotnet run --project $env:taskProject --no-launch-profile --urls 'http://localhost:5225' -- '--Database:MigrateOnStartup=true' '--Database:SeedOnStartup=true';" ^
    "exit $LASTEXITCODE;" ^
    "} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }"
set "taskServerExit=%ERRORLEVEL%"
popd
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
call :backendPrerequisites
if errorlevel 1 exit /b 1

if not exist "frontend\pages\login.html" (
    echo Khong tim thay frontend. Hay giu file BAT trong thu muc goc repository.
    exit /b 1
)

call :findPython
exit /b %ERRORLEVEL%

:backendPrerequisites
if not exist "RestaurantFoodManagerSystem\RestaurantFoodManagerSystem.csproj" (
    echo Khong tim thay project backend. Hay giu file BAT trong thu muc goc repository.
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Khong tim thay dotnet. Can cai .NET SDK 8 va mo lai file nay.
    exit /b 1
)

exit /b 0

:failed
pause
exit /b 1

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
