param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [string]$NodeExecutable
)

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$wpf = Join-Path $framework 'WPF'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework 4.x C# compiler is required.' }

$cache = Join-Path $PSScriptRoot '.build'
$source = Join-Path $PSScriptRoot 'src'
$out = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$exe = Join-Path $out 'STEP-3D-Viewer.exe'
$converter = Join-Path $cache 'convert.cjs'
$sample = Join-Path $cache 'sample.mesh.gz'
$licenses = Join-Path $cache 'licenses.txt'
$dependencyArguments = @((Join-Path $PSScriptRoot 'tools/prepare-dependencies.py'), $cache)
if ($NodeExecutable) { $dependencyArguments += @('--node', (Resolve-Path -LiteralPath $NodeExecutable).Path) }
& python @dependencyArguments
if ($LASTEXITCODE -ne 0) { throw 'Could not prepare the runtime dependencies.' }

Copy-Item -LiteralPath (Join-Path $source 'convert.cjs') -Destination $converter
& (Join-Path $cache 'node.exe') $converter --import (Join-Path $PSScriptRoot 'samples/stepped-block.step') $sample
if ($LASTEXITCODE -ne 0) { throw 'Could not convert the demo model.' }
$appLicense = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'LICENSE'))
$notices = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt'))
[System.IO.File]::WriteAllText($licenses, ($appLicense.TrimEnd() + "`r`n`r`n" + $notices))

$arguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/codepage:65001', '/optimize+', '/warnaserror+',
    "/out:$exe",
    "/reference:$(Join-Path $wpf 'PresentationCore.dll')",
    "/reference:$(Join-Path $wpf 'PresentationFramework.dll')",
    "/reference:$(Join-Path $wpf 'WindowsBase.dll')",
    "/reference:$(Join-Path $framework 'System.Xaml.dll')",
    "/resource:$(Join-Path $source 'Layout.xaml'),Viewer.Layout",
    "/resource:$sample,Viewer.Sample",
    "/resource:$(Join-Path $cache 'node.exe.gz'),Viewer.Node",
    "/resource:$converter,Viewer.Converter",
    "/resource:$(Join-Path $cache 'occt/occt-import-js.js'),Viewer.OcctJs",
    "/resource:$(Join-Path $cache 'occt/occt-import-js.wasm'),Viewer.OcctWasm",
    "/resource:$licenses,Viewer.Licenses",
    "/win32icon:$(Join-Path $source 'viewer.ico')",
    (Join-Path $source 'Viewer.cs'),
    (Join-Path $source 'Viewer.Files.cs'),
    (Join-Path $source 'Viewer.Language.cs'),
    (Join-Path $source 'StepImporter.cs')
)
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'The viewer did not compile.' }
Write-Output $exe
