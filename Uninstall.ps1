$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'ChatRailStandalone'))
$taskShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Chat Navigation Rail.lnk'
if (Test-Path -LiteralPath $taskShortcut) {
 $taskPreviousExe = (New-Object -ComObject WScript.Shell).CreateShortcut($taskShortcut).TargetPath
 if ([IO.Path]::GetFileName($taskPreviousExe) -eq 'RailWatcher.exe' -and [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($taskPreviousExe)) -eq 'ChatRailStandalone' -and $taskPreviousExe.StartsWith([IO.Path]::GetFullPath($env:USERPROFILE)+'\',[StringComparison]::OrdinalIgnoreCase)) { $taskRoot = [IO.Path]::GetDirectoryName($taskPreviousExe) }
}
$taskExisting = Get-ScheduledTask -TaskName ChatGPTChatNavigationRail -ErrorAction SilentlyContinue
if ($taskExisting) {
 $taskExe = $taskExisting.Actions[0].Execute
 if ([IO.Path]::GetFileName($taskExe) -ne 'RailWatcher.exe' -or [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($taskExe)) -ne 'ChatRailStandalone' -or !$taskExe.StartsWith([IO.Path]::GetFullPath($env:USERPROFILE)+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Task is not owned by this tool' }
 $taskRoot = [IO.Path]::GetDirectoryName($taskExe)
 Unregister-ScheduledTask -TaskName ChatGPTChatNavigationRail -Confirm:$false
}
$taskRun = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$taskValue = (Get-ItemProperty -Path $taskRun -Name ChatNavigationRail -ErrorAction SilentlyContinue).ChatNavigationRail
if ($taskValue -eq ('"'+(Join-Path $taskRoot 'RailWatcher.exe')+'"')) { Remove-ItemProperty -Path $taskRun -Name ChatNavigationRail }
foreach ($taskProc in @(Get-Process RailWatcher -ErrorAction SilentlyContinue)) {
 if ($taskProc.Path -eq (Join-Path $taskRoot 'RailWatcher.exe')) { Stop-Process -Id $taskProc.Id -Force }
}
# Ask the worker to detach the navigation before it exits.
if (Test-Path -LiteralPath $taskRoot) {
 if (@(Get-ChildItem -LiteralPath $taskRoot -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -or ((Get-Item -LiteralPath $taskRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Cleanup directory contains links' }
 New-Item -ItemType File -Path (Join-Path $taskRoot 'uninstall.signal') -Force | Out-Null
 for ($taskWait=0; $taskWait -lt 100; $taskWait++) {
  $taskWorkers = @(Get-Process ChatRailHost -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $taskRoot 'ChatRailHost.exe') })
  if ($taskWorkers.Count -eq 0) { break }; Start-Sleep -Milliseconds 300
 }
 foreach ($taskProc in $taskWorkers) { Stop-Process -Id $taskProc.Id -Force }
 if ([IO.Path]::GetFileName($taskRoot) -ne 'ChatRailStandalone' -or !$taskRoot.StartsWith([IO.Path]::GetFullPath($env:USERPROFILE)+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected cleanup path' }
 # Restore only pinned icon references owned by this tool before removing its files.
 $taskPins = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'
 $taskShell = New-Object -ComObject WScript.Shell
 foreach ($taskPin in @(Get-ChildItem -LiteralPath $taskPins -Filter '*.lnk' -ErrorAction SilentlyContinue)) {
  $taskLink = $taskShell.CreateShortcut($taskPin.FullName)
  if ($taskLink.IconLocation -eq ((Join-Path $taskRoot 'OfficialChatGPT.ico')+',0')) { $taskLink.IconLocation=$taskLink.TargetPath+',0';$taskLink.Save() }
 }
 Remove-Item -LiteralPath $taskRoot -Recurse -Force
}
$taskShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Chat Navigation Rail.lnk'
if (Test-Path -LiteralPath $taskShortcut) {
 $taskLink = (New-Object -ComObject WScript.Shell).CreateShortcut($taskShortcut)
 if ($taskLink.TargetPath -eq (Join-Path $taskRoot 'RailWatcher.exe')) { Remove-Item -LiteralPath $taskShortcut -Force }
}
Write-Host '卸载成功。' 
