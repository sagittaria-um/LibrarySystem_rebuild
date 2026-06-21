$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."
$out = Join-Path $root "deploy\out"
$serverOut = Join-Path $out "server"
$desktopOut = Join-Path $out "desktop"

Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $serverOut, $desktopOut | Out-Null

Push-Location (Join-Path $root "client")
npm install
npm run build
Pop-Location

dotnet publish (Join-Path $root "server\LibrarySystem.Api.csproj") -c Release -o $serverOut
dotnet publish (Join-Path $root "desktop\LibrarySystem.Desktop.csproj") -c Release -r win-x64 --self-contained false -o $desktopOut

Copy-Item -Recurse -Force (Join-Path $root "database") (Join-Path $out "database")
Copy-Item -Recurse -Force (Join-Path $root "docs") (Join-Path $out "docs")
Copy-Item -Force (Join-Path $root "deploy\docker-compose.yml") (Join-Path $out "docker-compose.yml")

Write-Host "Release files generated at $out"
