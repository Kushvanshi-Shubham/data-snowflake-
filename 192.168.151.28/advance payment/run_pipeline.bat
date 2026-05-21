@echo off
:: ============================================================
:: run_advance_payment_pipeline.bat
:: Schedule this via Windows Task Scheduler to run daily
:: ============================================================

SET EXE_PATH=C:\RFC_Pipelines\RFC_ZADVANCE_PAYMENT\RFC_ZADVANCE_PAYMENT.exe
SET LOG_PATH=C:\RFC_Pipelines\Logs\ZADVANCE_PAYMENT_%DATE:~-4%%DATE:~3,2%%DATE:~0,2%.log

echo [%DATE% %TIME%] Starting ZADVANCE_PAYMENT_RFC pipeline >> "%LOG_PATH%"

:: Run for yesterday (default — no args needed, handled in code)
"%EXE_PATH%" >> "%LOG_PATH%" 2>&1

:: OR run for a specific date range:
:: "%EXE_PATH%" 20250101 20250331 1000 >> "%LOG_PATH%" 2>&1

echo [%DATE% %TIME%] Pipeline complete. Exit code: %ERRORLEVEL% >> "%LOG_PATH%"

exit /b %ERRORLEVEL%
