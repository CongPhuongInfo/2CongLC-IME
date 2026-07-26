@echo off
setlocal

rem Build kieu framework-dependent: thu muc output gon (chi vai file: exe,
rem DLL cua rieng project, DLL ONNX), nhung MAY CHAY IME PHAI TU CAI .NET 9
rem Desktop Runtime truoc (nhe hon SDK day du, chi de CHAY chuong trinh):
rem https://dotnet.microsoft.com/download/dotnet/9.0
rem   -> chon "Desktop Runtime" (khong phai SDK) dung kien truc (x86).
rem
rem Dung cach nay neu ban se cai .NET Runtime san tren cac may dinh chay IME,
rem hoac muon thu muc build gon nhe de kiem tra nhanh. Neu muon 1 file duy
rem nhat khong can cai gi tren may khac, dung build-singlefile.bat thay the.

set "ROOT=%~dp0"
set "PROJ=%ROOT%src\VietnameseIME.vbproj"
set "OUT=%ROOT%bin"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Khong tim thay dotnet. Cai .NET 9 SDK tai:
    echo         https://dotnet.microsoft.com/download/dotnet/9.0
    exit /b 1
)

echo [INFO] Dang publish - framework-dependent (co the mat vai phut lan dau)...
dotnet publish "%PROJ%" -c Release -r win-x86 ^
    --self-contained false ^
    -o "%OUT%"

if errorlevel 1 (
    echo.
    echo [FAILED] Build that bai - xem loi phia tren.
    exit /b 1
)

if exist "%ROOT%src\bin" rmdir /s /q "%ROOT%src\bin"
if exist "%ROOT%src\obj" rmdir /s /q "%ROOT%src\obj"

echo.
echo [OK] Build thanh cong: %OUT%\VietnameseIME.exe
echo      (may chay IME can cai san .NET 9 Desktop Runtime)
endlocal
