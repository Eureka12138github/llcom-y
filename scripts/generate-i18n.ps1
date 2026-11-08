<#
.SYNOPSIS
    从 i18n/messages.json 生成 llcom/languages/*.xaml。

.DESCRIPTION
    JSON 是唯一翻译源；生成的 XAML 仍提交到仓库，运行时不依赖 JSON。
    生成结果固定使用 UTF-8（无 BOM）、CRLF、4 空格缩进，保证 diff 稳定。

.PARAMETER Check
    只校验生成结果是否与仓库中的 XAML 一致，不写文件。
    用于本地检查和 CI。

.EXAMPLE
    pwsh ./scripts/generate-i18n.ps1
    pwsh ./scripts/generate-i18n.ps1 -Check
#>
param(
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$jsonPath = Join-Path $repoRoot "i18n/messages.json"
$languageDir = Join-Path $repoRoot "llcom/languages"

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function ConvertTo-XamlText([string]$Value) {
    # 与手写 XAML 保持一致：只转义 XML 必需字符，换行统一为 &#10;
    $escaped = $Value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
    return $escaped.Replace("`r`n", "&#10;").Replace("`r", "&#10;").Replace("`n", "&#10;")
}

if (-not (Test-Path -LiteralPath $jsonPath)) {
    Fail "找不到翻译源文件：$jsonPath"
}

try {
    $data = Get-Content -LiteralPath $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
}
catch {
    Fail "i18n/messages.json 不是合法 JSON：$($_.Exception.Message)"
}

if ($null -eq $data -or $data.schema -ne 1) {
    Fail "i18n/messages.json 的 schema 必须为 1"
}
if ($null -eq $data.languages -or $data.languages.Count -eq 0) {
    Fail "i18n/messages.json 缺少 languages"
}
if ($null -eq $data.messages -or $data.messages.PSObject.Properties.Count -eq 0) {
    Fail "i18n/messages.json 缺少 messages"
}
if ($data.languages -notcontains $data.defaultLanguage) {
    Fail "defaultLanguage 必须包含在 languages 中"
}

$languages = @($data.languages | ForEach-Object { [string]$_ })
foreach ($language in $languages) {
    if ($language -notmatch "^[A-Za-z0-9-]+$") {
        Fail "语言代码不合法：$language"
    }
}

$messageProperties = @($data.messages.PSObject.Properties)
$seen = @{}
$errors = New-Object System.Collections.Generic.List[string]

foreach ($message in $messageProperties) {
    $key = [string]$message.Name
    if ([string]::IsNullOrWhiteSpace($key)) {
        $errors.Add("存在空的翻译 key")
        continue
    }
    if ($seen.ContainsKey($key)) {
        $errors.Add("重复的翻译 key：$key")
    }
    $seen[$key] = $true

    $values = $message.Value
    if ($null -eq $values) {
        $errors.Add("$key 没有任何语言值")
        continue
    }
    foreach ($language in $languages) {
        $property = $values.PSObject.Properties[$language]
        if ($null -eq $property) {
            $errors.Add("$key 缺少 $language")
        }
        elseif ([string]::IsNullOrEmpty([string]$property.Value)) {
            $errors.Add("$key 的 $language 为空")
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    Fail "翻译源校验失败，共 $($errors.Count) 个问题"
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$failed = $false

foreach ($language in $languages) {
    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append("<ResourceDictionary`r`n")
    [void]$builder.Append("    xmlns=`"http://schemas.microsoft.com/winfx/2006/xaml/presentation`"`r`n")
    [void]$builder.Append("    xmlns:x=`"http://schemas.microsoft.com/winfx/2006/xaml`"`r`n")
    [void]$builder.Append("    xmlns:system=`"clr-namespace:System;assembly=mscorlib`"`r`n")
    [void]$builder.Append("    xml:space=`"preserve`">`r`n")

    foreach ($message in $messageProperties) {
        $key = ConvertTo-XamlText ([string]$message.Name)
        $value = ConvertTo-XamlText ([string]$message.Value.PSObject.Properties[$language].Value)
        [void]$builder.Append("    <system:String x:Key=`"$key`">$value</system:String>`r`n")
    }

    [void]$builder.Append("</ResourceDictionary>`r`n")
    $content = $builder.ToString()
    $target = Join-Path $languageDir "$language.xaml"

    if ($Check) {
        if (-not (Test-Path -LiteralPath $target)) {
            Write-Host "缺少生成文件：$target" -ForegroundColor Red
            $failed = $true
            continue
        }
        $existing = [IO.File]::ReadAllText($target, [Text.Encoding]::UTF8)
        # 不同平台的 git 换行设置不同，校验时忽略 CRLF/LF 差异
        $normalizedExisting = $existing.Replace("`r`n", "`n").Replace("`r", "`n")
        $normalizedContent = $content.Replace("`r`n", "`n").Replace("`r", "`n")
        if ($normalizedExisting -ne $normalizedContent) {
            Write-Host "生成文件与 i18n/messages.json 不一致：$target" -ForegroundColor Red
            Write-Host "  请运行：pwsh ./scripts/generate-i18n.ps1" -ForegroundColor Yellow
            $failed = $true
        }
        continue
    }

    [IO.File]::WriteAllText($target, $content, $utf8NoBom)
    Write-Host "已生成：$target"
}

if ($failed) {
    exit 1
}

if ($Check) {
    Write-Host "i18n 校验通过：$($messageProperties.Count) 个 key，$($languages.Count) 种语言"
}
