#requires -Version 5.1
<#
.SYNOPSIS
生成与 WapProj/Package.appxmanifest 中 Publisher 匹配的 MSIX 侧载签名证书。

.DESCRIPTION
默认 Publisher 为 CN=Eureka12138github（见 Package.appxmanifest）。脚本会：
  1. 在 CurrentUser\My 创建自签名代码签名证书（默认 5 年）；
  2. 导出公钥 LLCOM-Y.cer（可分发给用户安装到「受信任人」）；
  3. 导出私钥 LLCOM-Y.pfx（不要提交，.gitignore 已忽略）；
  4. 生成随机密码写入 LLCOM-Y.pfx.password.txt（不要提交）；
  5. 打印指纹与下一步操作。

.NOTES
修改 Package.appxmanifest 的 Publisher 后必须重新生成证书，二者必须完全一致。
仅发布 exe 时不需要本脚本。
#>
param(
    [string]$Subject = "CN=Eureka12138github",
    [int]$Years = 5
)

$ErrorActionPreference = "Stop"
$outDir = $PSScriptRoot

# 清理同名旧证书（仅当前用户的「个人」存储，且只匹配本项目的友好名）
Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $Subject -and $_.FriendlyName -like "LLCOM-Y*" } |
    Remove-Item -Force -ErrorAction SilentlyContinue

$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -KeyUsage DigitalSignature `
    -FriendlyName "LLCOM-Y self-signed (sideload)" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter (Get-Date).AddYears($Years)

$passwordText = [Guid]::NewGuid().ToString("N").Substring(0, 20)
$password = ConvertTo-SecureString -String $passwordText -Force -AsPlainText

Export-PfxCertificate -Cert $cert -FilePath (Join-Path $outDir "LLCOM-Y.pfx") -Password $password | Out-Null
Export-Certificate -Cert $cert -FilePath (Join-Path $outDir "LLCOM-Y.cer") | Out-Null
Set-Content -Path (Join-Path $outDir "LLCOM-Y.pfx.password.txt") -Value $passwordText -Encoding ASCII

Write-Host ""
Write-Host "已生成自签名证书：" -ForegroundColor Green
Write-Host "  Subject    : $($cert.Subject)"
Write-Host "  Thumbprint : $($cert.Thumbprint)"
Write-Host "  Expires    : $($cert.NotAfter)"
Write-Host "  CER        : LLCOM-Y.cer（可分发；安装到「受信任人」）"
Write-Host "  PFX        : LLCOM-Y.pfx（私钥，勿提交；密码见 LLCOM-Y.pfx.password.txt）"
Write-Host ""
Write-Host "下一步（侧载）："
Write-Host "  1) 安装 VS 打包组件：VS Installer → 修改 → 单个组件 → 搜索「MSIX」/「Windows 应用打包项目」"
Write-Host "  2) 打开 WapProj，右键 → 发布 → 创建应用包，选择证书："
Write-Host "     - 从存储选择：$($cert.Subject)（指纹 $($cert.Thumbprint)）"
Write-Host "     - 或选择文件：WapProj\LLCOM-Y.pfx（使用上面的密码）"
Write-Host "  3) 用户安装：先双击 LLCOM-Y.cer → 安装证书 → 本地计算机 →「受信任人」，再安装 .msix"
Write-Host ""
