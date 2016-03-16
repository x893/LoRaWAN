@ECHO OFF

cd ..\..\packet_forwarder\lora_pkt_fwd

SET PATH=C:\Tools\GnuARM\RPi\bin;%PATH%
SET FIND="%SystemRoot%\System32\findstr.exe"

IF "%1*"=="clean*" GOTO :CLEAN
IF "%1*"=="rebuild*" CALL :REBUILD

:BUILD
make CROSS_COMPILE=arm-linux-gnueabihf- all 2>build.log | %FIND% /V "### Generate object file" | %FIND% /V "### Generate dependency file"
CALL :CHECK
GOTO :DONE

:REBUILD
CALL :CLEAN
GOTO :BUILD

:CLEAN
make CROSS_COMPILE=arm-linux-gnueabihf- clean 2>build.log
GOTO :DONE

:CHECK
IF NOT EXIST build.log GOTO :CHECK_DONE
%FIND% /C:": warning:" build.log > error.log
%FIND% /C:": error:" build.log  >> error.log
FOR /F "tokens=1,2,3,4,* delims=:" %%i IN (error.log) DO ECHO %%~dpnxi(%%j,%%k):%%l GCC0000:%%m
IF EXIST build.log del /Q build.log
:CHECK_DONE
IF EXIST error.log del /Q error.log

:DONE
