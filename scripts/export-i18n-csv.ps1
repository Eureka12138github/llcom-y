<#
.SYNOPSIS
    将 i18n/messages.json 导出为 CSV，便于用 Excel 审校翻译。

.DESCRIPTION
    JSON 仍是唯一翻译源；CSV 只是导出结果，不参与构建。
    默认输出到 i18n/messages.csv。

.PARAMETER OutputPath
    输出 CSV 路径，默认 i18n/messages.csv。

.EXAMPLE
    pwsh ./scripts/export-i18n-csv.ps1
#>
param(
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$jsonPath = Join-Path $repoRoot "i18n/messages.json"
if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot "i18n/messages.csv"
}

if (-not (Test-Path -LiteralPath $jsonPath)) {
    Write-Error "找不到翻译源文件：$jsonPath"
    exit 1
}

$data = Get-Content -LiteralPath $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$languages = @($data.languages | ForEach-Object { [string]$_ })

$rows = foreach ($message in @($data.messages.PSObject.Properties)) {
    $row = [ordered]@{ Key = [string]$message.Name }
    foreach ($language in $languages) {
        $row[$language] = [string]$message.Value.PSObject.Properties[$language].Value
    }
    $note = $message.Value.PSObject.Properties["note"]
    $row["note"] = if ($null -ne $note) { [string]$note.Value } else { "" }
    [pscustomobject]$row
}

$rows | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
Write-Host "已导出：$OutputPath（$($rows.Count) 个 key）"
