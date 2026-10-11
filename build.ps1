param([string]$OutputDirectory = 'build/windows', [switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Windows .NET Framework C# compiler was not found.' }
$outputDir = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $projectRoot $OutputDirectory }
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$defines = @()
if ($Test) {
    $sourceFiles += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests/windows') -Filter '*.cs' | ForEach-Object { $_.FullName })
    $defines = @('/define:SELF_TEST')
}
$iconPath = Join-Path $projectRoot 'assets\app.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { & (Join-Path $projectRoot 'build-icon.ps1') }
$programPath = Join-Path $outputDir 'AI-Image-Editing-Tools.exe'
$defines += @("/resource:$projectRoot\ai\local_qr.py,AiQrWorker", "/resource:$projectRoot\ai\models.json,AiQrModels")
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /codepage:65001 "/out:$programPath" "/win32icon:$iconPath" "/resource:$iconPath,AppIcon" "/resource:$projectRoot\assets\en.json,EnglishStrings" "/resource:$projectRoot\assets\tutorial.zh.json,TutorialChinese" "/resource:$projectRoot\assets\tutorial.en.json,TutorialEnglish" "/resource:$projectRoot\LICENSE,ProjectLicense" "/resource:$projectRoot\THIRD_PARTY_NOTICES.md,ThirdPartyNotices" "/reference:$projectRoot\vendor\zxing\zxing-net.dll" "/resource:$projectRoot\vendor\zxing\zxing-net.dll,ZxingAssembly" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll $defines $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $programPath"
