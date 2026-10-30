@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "WPF=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF"
if not exist "%CSC%" (
  echo .NET Framework 4.x compiler not found.
  exit /b 1
)
if not exist build mkdir build
"%CSC%" /nologo /target:winexe /optimize+ /out:build\DouyinLikes.exe /resource:src\unified-collector.js,collector.js /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:"%WPF%\PresentationCore.dll" /reference:"%WPF%\WindowsBase.dll" /reference:System.Xaml.dll src\UnifiedCore.cs src\UnifiedFormBuilt.cs
if errorlevel 1 exit /b %errorlevel%
echo Built build\DouyinLikes.exe
