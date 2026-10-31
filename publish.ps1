# Publishes Tomaoke and adds a desktop shortcut.
# The shortcut starts the app through dotnet, because Smart App Control blocks the unsigned exe.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\KaraokeVox.App\KaraokeVox.App.csproj"
$dest = Join-Path $env:LOCALAPPDATA "Tomaoke"
dotnet publish $project -c Release -o $dest --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Join-Path $dest "Tomaoke.dll"
if (-not (Test-Path $dll)) { throw "Publish did not write $dll" }

$vbsPath = Join-Path $dest "Tomaoke.vbs"
@(
    "Set shell = CreateObject(""Wscript.Shell"")"
    "folder = CreateObject(""Scripting.FileSystemObject"").GetParentFolderName(WScript.ScriptFullName)"
    "shell.CurrentDirectory = folder"
    "shell.Run ""dotnet exec """""" & folder & ""\Tomaoke.dll"""""", 0, False"
) | Set-Content -Path $vbsPath -Encoding ASCII

$wscript = Join-Path $env:WINDIR "System32\wscript.exe"
$shell = New-Object -ComObject WScript.Shell
foreach ($dir in @(
    [Environment]::GetFolderPath("Desktop"),
    [Environment]::GetFolderPath("Programs")
)) {
    $link = $shell.CreateShortcut((Join-Path $dir "Tomaoke.lnk"))
    $link.TargetPath = $wscript
    $link.Arguments = "//B `"$vbsPath`""
    $link.WorkingDirectory = $dest
    $link.WindowStyle = 7
    $link.Description = "Tomaoke"
    $icon = Join-Path $dest "Tomaoke.exe"
    if (Test-Path $icon) { $link.IconLocation = "$icon,0" }
    $link.Save()
}

Write-Output "Published to $dest"
