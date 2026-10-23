$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$buildScript = Join-Path $root 'build.ps1'
$testSource = Join-Path $root 'tests\StorageLifecycleTests.cs'
$testDataRoot = Join-Path $root '.testdata'
$testRoot = Join-Path $testDataRoot ('regression-' + [Guid]::NewGuid().ToString('N'))
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'

& $buildScript

$references = @(
    (Join-Path $root 'bin\GhostDesk.exe'),
    (Join-Path $framework 'WPF\PresentationCore.dll'),
    (Join-Path $framework 'WPF\PresentationFramework.dll'),
    (Join-Path $framework 'WPF\WindowsBase.dll'),
    (Join-Path $framework 'System.Xaml.dll'),
    (Join-Path $framework 'System.Runtime.Serialization.dll'),
    (Join-Path $framework 'System.Windows.Forms.dll'),
    (Join-Path $framework 'System.Drawing.dll'),
    (Join-Path $framework 'System.Web.Extensions.dll'),
    (Join-Path $framework 'Microsoft.VisualBasic.dll'),
    (Join-Path $framework 'Microsoft.CSharp.dll')
)
$referenceArg = '/r:' + ($references -join ';')
$testAssembly = Join-Path $testRoot 'StorageLifecycleTests.exe'
$ghostDeskCopy = Join-Path $testRoot 'GhostDesk.exe'

New-Item -ItemType Directory -Path $testRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'bin\GhostDesk.exe') -Destination $ghostDeskCopy

& $csc /nologo /target:exe /platform:anycpu /optimize+ /out:$testAssembly $referenceArg $testSource
if ($LASTEXITCODE -ne 0) {
    throw "Regression test compilation failed with exit code $LASTEXITCODE"
}

& $testAssembly
$testExitCode = $LASTEXITCODE

if ($testExitCode -eq 0 -and $env:GHOSTDESK_KEEP_TESTDATA -ne '1') {
    $resolvedDataRoot = [IO.Path]::GetFullPath($testDataRoot)
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $requiredPrefix = $resolvedDataRoot.TrimEnd('\') + '\'
    if (-not $resolvedTestRoot.StartsWith(
            $requiredPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean unexpected test path: $resolvedTestRoot"
    }

    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
}

if ($testExitCode -ne 0) {
    Write-Host "Regression workspace retained: $testRoot"
    exit $testExitCode
}

Write-Host "Regression tests passed in isolated workspace."
