#requires -Version 5.1
<#
.SYNOPSIS
本地生成发布 zip（不依赖 GitHub Actions）。

.DESCRIPTION
构建 Release，并把运行所需文件打包到 artifacts\release\llcom-y-<version>-<platform>.zip。
只包含构建产物 + core_script + 许可证 + CHANGES；
**不会**打进本机的 settings.json / logs / 用户脚本。

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1 -Version 2.0.0.0 -Platform x86
#>
param(
    [string]$Version = "2.0.0.0",
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

    Copy-Item (Join-Path $src "llcom.exe") $stage
    $cfg = Join-Path $src "llcom.exe.config"
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
    $zip = Join-Path $outDir "llcom-y-$Version-$Platform.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
    Remove-Item $stage -Recurse -Force

    Write-Host ""
    Write-Host "已生成: $zip" -ForegroundColor Green
    Write-Host "上传到 GitHub Release 即可；或推送 tag 后由 CI 自动完成。"
}
finally {
    Pop-Location
}
