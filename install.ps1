# Publish MouseClicker and install it to %LOCALAPPDATA%\Programs\MouseClicker.
# Re-run to update an existing install. Creates a Start Menu shortcut.
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\MouseClicker\MouseClicker.csproj'
$publish = Join-Path $PSScriptRoot 'publish'
$target = Join-Path $env:LOCALAPPDATA 'Programs\MouseClicker'
$exe = Join-Path $target 'MouseClicker.exe'

Get-Process MouseClicker -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

dotnet publish $project -c Release -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $publish '*') -Destination $target -Recurse -Force

$shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Mouse Clicker.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.Description = 'Mouse Clicker'
$shortcut.Save()

Start-Process -FilePath $exe -WorkingDirectory $target
Write-Output "Installed to $target"
