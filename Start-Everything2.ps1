$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet'
dotnet build (Join-Path $projectRoot 'Everything2.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Start-Process -FilePath (Join-Path $projectRoot 'bin\Release\net10.0-windows\Everything2.exe') -WorkingDirectory $projectRoot
