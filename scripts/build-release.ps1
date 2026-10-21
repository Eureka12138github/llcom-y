#requires -Version 5.1
<#
.SYNOPSIS
本地生成发布 zip（不依赖 GitHub Actions）。

.DESCRIPTION
构建 Release，并生成 x64 / x86 发布包：artifacts\release\llcom-y-x64.zip 或 llcom-y-x86.zip。
只包含构建产物 + core_script + 许可证 + CHANGES；
**不会**打进本机的 settings.json / logs / 用户脚本。
自动更新清单：x64 用 autoUpdate.xml，x86 用 autoUpdate-x86.xml。

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1 -Version 2.0.0.2 -Platform x86
#>
param(
    [string]$Version = "2.0.0.2",
    [ValidateSet("x64", "x86")]
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet build llcom\llcom.csproj -c Release -p:Platform=$Platform
    if ($LASTEXITCODE -ne 0) { throw "构建失败（exit $LASTEXITCODE）" }

    $stage = Join-Path $env:TEMP ("llcom-y-stage-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    $src = "artifacts\Release\$Platform"

    Copy-Item (Join-Path $src "llcom-y.exe") $stage
    $cfg = Join-Path $src "llcom-y.exe.config"
    if (Test-Path $cfg) { Copy-Item $cfg $stage }

    # 默认 Lua 脚本（首启也会从内嵌资源释放，这里预置一份）
    Copy-Item "llcom\DefaultFiles\core_script" (Join-Path $stage "core_script") -Recurse

    # 许可证 / 版本说明
    Copy-Item "LICENSE" (Join-Path $stage "LICENSE.txt")
    Copy-Item "CHANGES.md" $stage
    if (Test-Path "llcom\DefaultFiles\LICENSE") {
        Copy-Item "llcom\DefaultFiles\LICENSE" (Join-Path $stage "LICENSE-default-scripts.txt")
    }

    $outDir = "artifacts\release"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $assetName = "llcom-y-$Platform.zip"
    $zip = Join-Path $outDir $assetName
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
    Remove-Item $stage -Recurse -Force

    # 自动更新清单：x64 用 autoUpdate.xml，x86 用 autoUpdate-x86.xml
    $tag = "v$Version"
    $manifest = @(
        '<?xml version="1.0" encoding="UTF-8"?>',
        '<item>',
        "    <version>$Version</version>",
        "    <url>https://github.com/Eureka12138github/llcom-y/releases/download/$tag/$assetName</url>",
        "    <changelog>https://github.com/Eureka12138github/llcom-y/releases/tag/$tag</changelog>",
        '    <mandatory mode="1">false</mandatory>',
        '</item>'
    ) -join "`r`n"
    $manifestName = if ($Platform -eq "x64") { "autoUpdate.xml" } else { "autoUpdate-x86.xml" }
    $manifestPath = Join-Path $outDir $manifestName
    Set-Content -Path $manifestPath -Value $manifest -Encoding UTF8

    Write-Host ""
    Write-Host "已生成:" -ForegroundColor Green
    Write-Host "  $zip"
    Write-Host "  $manifestPath"
    Write-Host "把这两个文件一起上传到 GitHub Release；或推送 tag 后由 CI 自动完成。"
}
finally {
    Pop-Location
}
