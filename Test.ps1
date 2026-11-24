param([string]$Executable = (Join-Path $PSScriptRoot 'dist/STEP-3D-Viewer.exe'))

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$results = (New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot '.build/test')).FullName
$check = Join-Path $results 'check-viewer.exe'
$arguments = @(
    '/nologo', '/target:exe', '/platform:x64', '/codepage:65001', '/warnaserror+',
    "/out:$check",
    "/reference:$(Join-Path $wpf 'PresentationCore.dll')",
    "/reference:$(Join-Path $wpf 'PresentationFramework.dll')",
    "/reference:$(Join-Path $wpf 'WindowsBase.dll')",
    "/reference:$(Join-Path $framework 'System.Xaml.dll')",
    (Join-Path $PSScriptRoot 'tests/check-viewer.cs')
)
& (Join-Path $framework 'csc.exe') @arguments
if ($LASTEXITCODE -ne 0) { throw 'The viewer tests did not compile.' }
& $check (Resolve-Path -LiteralPath $Executable).Path $results (Join-Path $PSScriptRoot 'samples/stepped-block.step')
if ($LASTEXITCODE -ne 0) { throw 'The viewer did not pass off-screen verification.' }
