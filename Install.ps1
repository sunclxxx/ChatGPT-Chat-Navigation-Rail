$ErrorActionPreference = 'Stop'
$taskRoot = Join-Path $env:LOCALAPPDATA 'ChatRailStandalone'
$taskRoot = [IO.Path]::GetFullPath($taskRoot)
$taskShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Chat Navigation Rail.lnk'
if (Test-Path -LiteralPath $taskShortcut) {
 $taskPreviousExe = (New-Object -ComObject WScript.Shell).CreateShortcut($taskShortcut).TargetPath
 if ([IO.Path]::GetFileName($taskPreviousExe) -eq 'RailWatcher.exe' -and [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($taskPreviousExe)) -eq 'ChatRailStandalone' -and $taskPreviousExe.StartsWith([IO.Path]::GetFullPath($env:USERPROFILE)+'\',[StringComparison]::OrdinalIgnoreCase)) { $taskRoot = [IO.Path]::GetDirectoryName($taskPreviousExe) }
}
$taskExisting = Get-ScheduledTask -TaskName ChatGPTChatNavigationRail -ErrorAction SilentlyContinue
if ($taskExisting) {
 $taskExistingExe = $taskExisting.Actions[0].Execute
 if ([IO.Path]::GetFileName($taskExistingExe) -ne 'RailWatcher.exe' -or [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($taskExistingExe)) -ne 'ChatRailStandalone' -or !$taskExistingExe.StartsWith([IO.Path]::GetFullPath($env:USERPROFILE)+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Existing task belongs to another program' }
 $taskRoot = [IO.Path]::GetDirectoryName($taskExistingExe)
 Unregister-ScheduledTask -TaskName ChatGPTChatNavigationRail -Confirm:$false
}
if ((Test-Path -LiteralPath $taskRoot) -and ((Get-Item -LiteralPath $taskRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Install directory is a link' }
if ((Test-Path -LiteralPath $taskRoot) -and @(Get-ChildItem -LiteralPath $taskRoot -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Install directory contains links' }
# Stop only the listener; allow the worker to detach its UI before replacing files.
if (Test-Path -LiteralPath $taskRoot) {
 foreach ($taskProc in @(Get-Process RailWatcher -ErrorAction SilentlyContinue)) {
  if ($taskProc.Path -eq (Join-Path $taskRoot 'RailWatcher.exe')) { Stop-Process -Id $taskProc.Id -Force; [void]$taskProc.WaitForExit(5000) }
 }
 [IO.File]::WriteAllText((Join-Path $taskRoot 'uninstall.signal'),'')
 for ($taskWait=0; $taskWait -lt 50; $taskWait++) {
  $taskWorkers = @(Get-Process ChatRailHost -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $taskRoot 'ChatRailHost.exe') })
  if ($taskWorkers.Count -eq 0) { break }; Start-Sleep -Milliseconds 300
 }
 if ($taskWorkers.Count -gt 0) { throw '请先退出已有导航工具后再安装。' }
}
foreach ($taskProc in @(Get-Process RailWatcher,ChatRailHost -ErrorAction SilentlyContinue)) {
 try { if ($taskProc.Path -eq (Join-Path $taskRoot ($taskProc.ProcessName+'.exe')) -or $taskProc.Path -eq ([IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA ('ChatRailStandalone\'+$taskProc.ProcessName+'.exe'))))) { Stop-Process -Id $taskProc.Id -Force; [void]$taskProc.WaitForExit(5000) } } catch { throw '请先退出已有导航工具后再安装。' }
}
New-Item -ItemType Directory -Path $taskRoot -Force | Out-Null
Remove-Item -LiteralPath (Join-Path $taskRoot 'uninstall.signal') -Force -ErrorAction SilentlyContinue
foreach ($taskName in @('RailWatcher.exe','ChatRailHost.exe','navigation.js','NavigationRail.ico','adapter.json','RefreshAdapter.ps1','README.md','Uninstall.ps1','Uninstall.exe')) {
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot $taskName) -Destination (Join-Path $taskRoot $taskName) -Force
}
$taskImages = Join-Path $taskRoot 'docs\images'
New-Item -ItemType Directory -Path $taskImages -Force | Out-Null
foreach ($taskImage in @('overview.png','preview-bookmark.png','history-loading.png')) {
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('docs\images\'+$taskImage)) -Destination (Join-Path $taskImages $taskImage) -Force
}
# Resolve MSIX AppData virtualization before handing the path to Windows.
Add-Type -TypeDefinition @'
using System;using System.IO;using System.Text;using System.Runtime.InteropServices;using Microsoft.Win32.SafeHandles;
public static class ChatRailInstallPath {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle h,StringBuilder s,uint n,uint flags);
 public static string Resolve(string path){using(var file=File.OpenRead(path)){var text=new StringBuilder(4096);if(GetFinalPathNameByHandle(file.SafeFileHandle,text,4096,0)==0)throw new System.ComponentModel.Win32Exception();string result=text.ToString();return result.StartsWith(@"\\?\")?result.Substring(4):result;}}
}
'@
$taskExe = [ChatRailInstallPath]::Resolve((Join-Path $taskRoot 'RailWatcher.exe'))
$taskRoot = [IO.Path]::GetDirectoryName($taskExe)
$taskRun = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$taskOldRun = (Get-ItemProperty -Path $taskRun -Name ChatNavigationRail -ErrorAction SilentlyContinue).ChatNavigationRail
if ($taskOldRun -and ($taskOldRun -eq ('"'+$taskExe+'"') -or $taskOldRun -eq ('"'+[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'ChatRailStandalone\RailWatcher.exe'))+'"'))) { Remove-ItemProperty -Path $taskRun -Name ChatNavigationRail }
$taskShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Chat Navigation Rail.lnk'
$taskShell = New-Object -ComObject WScript.Shell
$taskLink = $taskShell.CreateShortcut($taskShortcut)
$taskLink.TargetPath = $taskExe
$taskLink.Arguments = '--manual'
$taskLink.WorkingDirectory = $taskRoot
$taskIconName = 'NavigationRail-'+(Get-FileHash -LiteralPath (Join-Path $taskRoot 'NavigationRail.ico') -Algorithm SHA256).Hash.Substring(0,16)+'.ico'
$taskIconPath = Join-Path $taskRoot $taskIconName
Copy-Item -LiteralPath (Join-Path $taskRoot 'NavigationRail.ico') -Destination $taskIconPath -Force
$taskLink.IconLocation = $taskIconPath+',0'
$taskLink.WindowStyle = 7
$taskLink.Description = 'Chat Navigation Rail'
$taskLink.Save()
foreach ($taskIcon in @(Get-ChildItem -LiteralPath $taskRoot -Filter 'NavigationRail-*.ico')) {
 if ($taskIcon.Name -ne $taskIconName -and ($taskIcon.Name -eq 'NavigationRail-150.ico' -or $taskIcon.Name -match '^NavigationRail-[A-F0-9]{16}\.ico$')) { Remove-Item -LiteralPath $taskIcon.FullName -Force }
}
& "$env:WINDIR\System32\ie4uinit.exe" -show
foreach ($taskOldFile in @('automatic.disabled','manual.request','wait-current.once')) { Remove-Item -LiteralPath (Join-Path $taskRoot $taskOldFile) -Force -ErrorAction SilentlyContinue }
Write-Host '安装成功，已创建 Chat Navigation Rail 开始菜单快捷方式。'

# Remove the redundant direct-EXE entry only when the official package entry exists.
$taskLegacyLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'ChatGPT.lnk'
if ((Get-StartApps | Where-Object {$_.AppID -eq 'OpenAI.Codex_2p2nqsd0c76g0!App'}) -and (Test-Path -LiteralPath $taskLegacyLink)) {
 $taskLegacy = (New-Object -ComObject WScript.Shell).CreateShortcut($taskLegacyLink)
 if ($taskLegacy.TargetPath -match '\\WindowsApps\\OpenAI.Codex_.*\\app\\ChatGPT.exe$' -and !$taskLegacy.Arguments) { Remove-Item -LiteralPath $taskLegacyLink -Force }
}
