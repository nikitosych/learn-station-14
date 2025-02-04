@echo off
setlocal enabledelayedexpansion

:: where to search
set "ROOT_DIR=."

set "OUTPUT_FILE=merged.ftl"

:: delete if exists
if exist "%OUTPUT_FILE%" del "%OUTPUT_FILE%"

:: merge all .ftl files
for /r "%ROOT_DIR%" %%F in (*.ftl) do (
    echo. >> "%OUTPUT_FILE%"
    echo # -- Start of %%F -- >> "%OUTPUT_FILE%"
    type "%%F" >> "%OUTPUT_FILE%"
    echo. >> "%OUTPUT_FILE%"
    echo # -- End of %%F -- >> "%OUTPUT_FILE%"
)

echo Done, saved as %OUTPUT_FILE%
