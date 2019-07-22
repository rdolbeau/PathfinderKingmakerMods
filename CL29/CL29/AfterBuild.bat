del *.zip
rmdir /S /Q CL29

mkdir CL29 || goto :error
xcopy CL29.dll CL29 || goto :error
xcopy ..\..\Info.json CL29 || goto :error
"C:\Program Files\7-Zip\7z.exe" a CL29.zip CL29 || goto :error
copy /y CL29.zip "Z:\" || goto :error
goto :EOF

:error
echo Failed with error #%errorlevel%.
exit /b %errorlevel%