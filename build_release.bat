@echo off
"C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "C:\Users\Admin\TiaTracker\TiaTracker\TiaTracker.csproj" /p:Configuration=Release /t:Build /v:minimal
echo EXIT_CODE=%ERRORLEVEL%
