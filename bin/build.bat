@echo off
setlocal

rem Build bang .NET SDK (dotnet publish) thay cho vbc.exe thu cong. NuGet tu
rem lo Microsoft.ML.OnnxRuntime (ca managed DLL lan native onnxruntime.dll)
rem nen KHONG can setup_onnx.bat / nuget.exe / netstandard.dll nua.
rem
rem --self-contained true (khai bao san trong .vbproj) dong goi luon .NET
rem runtime vao thu muc output, nguoi dung KHONG can cai .NET Desktop Runtime
rem rieng - giu dung tinh than "khong can cai gi them" cua project ban dau.

set "BIN=%~dp0"
set "PROJ=%~dp0..\src\VietnameseIME.vbproj"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Khong tim thay dotnet. Cai .NET 9 SDK tai:
    echo         https://dotnet.microsoft.com/download/dotnet/9.0
    exit /b 1
)

echo [INFO] Dang publish (co the mat vai phut lan dau, dotnet tai NuGet packages)...
dotnet publish "%PROJ%" -c Release -r win-x86 -o "%BIN%"

if errorlevel 1 (
    echo.
    echo [FAILED] Build that bai - xem loi phia tren.
    exit /b 1
)

echo.
echo [OK] Build thanh cong: %BIN%VietnameseIME.exe
endlocal
