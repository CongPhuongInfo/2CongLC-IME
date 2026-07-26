@echo off
setlocal

rem Build ra 1 file .exe duy nhat, tu chua .NET runtime ben trong (self-
rem contained) - may chay IME KHONG can cai .NET gi ca, giu dung tinh than
rem "khong can cai them gi" cua ban goc dung vbc.exe.
rem
rem Doi lai: file .exe se to hon nhieu (runtime nen ben trong), va van co
rem 1 file rieng ben canh la onnxruntime.dll (native runtime cua ONNX,
rem khong nhet vao file gop duoc, ONNX Runtime tu load no luc chay).

set "ROOT=%~dp0"
set "PROJ=%ROOT%src\VietnameseIME.vbproj"
set "OUT=%ROOT%bin"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Khong tim thay dotnet. Cai .NET 9 SDK tai:
    echo         https://dotnet.microsoft.com/download/dotnet/9.0
    exit /b 1
)

echo [INFO] Dang publish - single file, self-contained (co the mat vai phut lan dau)...
dotnet publish "%PROJ%" -c Release -r win-x86 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o "%OUT%"

if errorlevel 1 (
    echo.
    echo [FAILED] Build that bai - xem loi phia tren.
    exit /b 1
)

rem Don thu muc tam dotnet tu sinh ra trong src\ (khong phai ket qua build,
rem chi la file trung gian) cho do roi mat.
if exist "%ROOT%src\bin" rmdir /s /q "%ROOT%src\bin"
if exist "%ROOT%src\obj" rmdir /s /q "%ROOT%src\obj"

echo.
echo [OK] Build thanh cong: %OUT%\VietnameseIME.exe
echo      (chi 1 file .exe + onnxruntime.dll ben canh)
endlocal
