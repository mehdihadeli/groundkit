@rem Repo-local development launcher for the GroundKit registry maintainer CLI.
@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%src\groundkit-registry\GroundKit.Registry.csproj"

dotnet run --project "%PROJECT%" --no-restore -v:q -p:NoWarn=NU1510%%3BNU1901 -- %*
exit /b %ERRORLEVEL%