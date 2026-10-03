$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $taskCompiler /nologo /target:winexe /platform:x64 /reference:System.Web.Extensions.dll /reference:System.Management.dll /main:LocalChatRail.Watcher "/out:$PSScriptRoot\RailWatcher.exe" "/win32icon:$PSScriptRoot\NavigationRail.ico" "$PSScriptRoot\RailWatcher.cs"
if ($LASTEXITCODE -ne 0) { throw 'Watcher build failed' }
& $taskCompiler /nologo /target:winexe /platform:x64 /main:LocalChatRail.Program "/out:$PSScriptRoot\ChatRailHost.exe" "/win32icon:$PSScriptRoot\NavigationRail.ico" /reference:System.Management.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "$PSScriptRoot\ChatRailHost.cs" "$PSScriptRoot\RailWatcher.cs" "$PSScriptRoot\RailUi.cs"
if ($LASTEXITCODE -ne 0) { throw 'Worker build failed' }
foreach($taskName in @('Install','Uninstall')) {
 & $taskCompiler /nologo /target:winexe /platform:x64 /main:LocalChatRail.Setup "/out:$PSScriptRoot\$taskName.exe" "/win32icon:$PSScriptRoot\NavigationRail.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "$PSScriptRoot\Setup.cs" "$PSScriptRoot\RailUi.cs"
 if ($LASTEXITCODE -ne 0) { throw 'Setup build failed' }
}
