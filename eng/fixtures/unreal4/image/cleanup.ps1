# Removes what an installer leaves behind, so the layer stays small.
Remove-Item -Recurse -Force 'C:\setup\dl', "$env:TEMP\*", 'C:\Windows\Temp\*', 'C:\ProgramData\Package Cache' -ErrorAction SilentlyContinue

# Exit with 0, because powershell -Command exits with 1 when its last statement fails quietly and a missing folder does that.
exit 0
