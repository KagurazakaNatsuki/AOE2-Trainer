param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'),
    [string]$IconPath,
    [string]$CoverPath
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework x64 C# compiler was not found.'
}

$source = Join-Path $PSScriptRoot 'AoE2DELatestTrainer.cs'
$localization = Join-Path $PSScriptRoot 'Localization.cs'
$manifest = Join-Path $PSScriptRoot 'admin.manifest'
if (-not (Test-Path -LiteralPath $source) -or -not (Test-Path -LiteralPath $localization) -or -not (Test-Path -LiteralPath $manifest)) {
    throw 'Source code, localization, or administrator manifest is missing.'
}

$outputDirectoryPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputDirectoryPath -Force | Out-Null
$outputFile = Join-Path $outputDirectoryPath 'AoE2DE.Plus12.exe'
$arguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    "/win32manifest:$manifest", "/out:$outputFile"
)

if ($IconPath) {
    $icon = (Resolve-Path -LiteralPath $IconPath -ErrorAction Stop).Path
    $arguments += "/win32icon:$icon"
    $arguments += "/resource:$icon,AoE.Icon"
}
if ($CoverPath) {
    $cover = (Resolve-Path -LiteralPath $CoverPath -ErrorAction Stop).Path
    $arguments += "/resource:$cover,AoE.Cover"
}
$arguments += $source
$arguments += $localization

& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "Compiler failed with exit code $LASTEXITCODE" }
Write-Output $outputFile
