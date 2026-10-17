param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'restore', 'test')]
    [string]$cmd = 'build',
    [ValidateSet('Debug', 'Release')]
    [string]$configuration = 'Release',
    [ValidateSet('x64', 'arm64', 'x86')]
    [string]$platform = 'x64'
)

$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param([string]$File, [string[]]$Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$File failed with exit code $LASTEXITCODE."
    }
}

if ($cmd -eq 'test') {
    Invoke-Checked -File 'dotnet' -Arguments @(
        'test', "$PSScriptRoot/test/AudioFlip.Tests.csproj",
        '-c', $configuration, "-p:Platform=$platform", '--logger', 'trx'
    )
    return
}

$msbuildCommand = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
if ($msbuildCommand) {
    $msbuild = $msbuildCommand.Source
} else {
    $vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    if (!(Test-Path -LiteralPath $vswhere)) {
        throw 'Install Visual Studio 2026 with the WinUI and Windows application packaging tools.'
    }
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (!$msbuild) {
        throw 'MSBuild was not found. Install the Visual Studio Windows application development tools.'
    }
}

$arguments = @(
    "$PSScriptRoot/AudioFlip.slnx", '-nologo', '-v:minimal',
    "-p:Configuration=$configuration", "-p:Platform=$platform"
)
if ($cmd -eq 'restore') {
    $arguments += '-t:Restore'
} else {
    $arguments += @(
        '-restore', '-p:AppxPackageSigningEnabled=false',
        "-p:AppxBundlePlatforms=$platform", '-p:AppxBundle=Always',
        '-p:UapAppxPackageBuildMode=SideloadOnly'
    )
}

Invoke-Checked -File $msbuild -Arguments $arguments
