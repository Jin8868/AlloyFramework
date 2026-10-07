[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityProjectRoot,
    [string]$Platform = 'Windows',
    [string]$Language = 'English(US)'
)

$ErrorActionPreference = 'Stop'
$unityRoot = [IO.Path]::GetFullPath($UnityProjectRoot)
[xml]$settings = Get-Content (Join-Path $unityRoot 'Assets/WwiseSettings.xml') -Raw -Encoding UTF8
$outputRoot = [IO.Path]::GetFullPath((Join-Path (Join-Path $unityRoot 'Assets') $settings.WwiseSettings.RootOutputPath))
if ($Platform -notmatch '^[A-Za-z0-9()-]+$') { throw '平台名称包含非法路径字符。' }
$sourceRoot = Join-Path $outputRoot $Platform
$projectInfo = Get-Content -LiteralPath (Join-Path $outputRoot 'ProjectInfo.json') -Raw -Encoding UTF8 |
    ConvertFrom-Json
$generatorVersion = [string]$projectInfo.ProjectInfo.Project.Generator
if ($generatorVersion -notmatch '^(\d+\.\d+\.\d+)\.(\d+)$') { throw '缺少 Wwise 生成器版本。' }
$backendVersion = $Matches[1] + ' Build ' + $Matches[2]
$platformInfo = Get-Content -LiteralPath (Join-Path $sourceRoot 'PlatformInfo.json') -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ($platformInfo.PlatformInfo.Platform.Generator -ne $generatorVersion -or
    $platformInfo.PlatformInfo.Settings.RemoveUnusedGeneratedFiles -ne 'true' -or
    $platformInfo.PlatformInfo.Settings.UseSoundBankNames -ne 'true' -or
    $platformInfo.PlatformInfo.Settings.SubFoldersForGeneratedFiles -ne 'false') {
    throw '生成设置不匹配：请启用移除未使用输出和 Bank 名称，并使用当前无散列子目录的输出布局。'
}
$destinationRoot = Join-Path $unityRoot "Assets/Res/WwiseAudio/$Platform"
$utf8 = New-Object System.Text.UTF8Encoding($false)
$groups = [ordered]@{}
$banks = [ordered]@{}
$events = [ordered]@{}
$allFiles = [ordered]@{}

function New-ContentFile([string]$relativePath) {
    if ([IO.Path]::IsPathRooted($relativePath) -or $relativePath.Contains(':') -or
        ($relativePath.Replace('\', '/').Split('/') -contains '..')) {
        throw "元数据包含非法相对路径：$relativePath"
    }
    $relativePath = $relativePath.Replace('\', '/')
    $filePath = Join-Path $sourceRoot $relativePath
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) { throw "音频文件缺失：$filePath" }
    $hash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
    $allFiles[$relativePath] = $hash
    return [ordered]@{ Path = $relativePath; Hash = $hash; Location = '' }
}

# 按实际安装版本的生成元数据建立依赖，不维护第二份人工 Event 表。
foreach ($metadata in Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter '*.json') {
    if ($metadata.Name -eq 'AlloyAudioManifest.json') { continue }
    $document = Get-Content -LiteralPath $metadata.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $document.SoundBanksInfo) { continue }
    if ($document.SoundBanksInfo.SchemaVersion -ne '16' -or
        $document.SoundBanksInfo.SoundBankVersion -ne '172') {
        throw "不支持此 Wwise 元数据版本，请核实导出器：$($metadata.FullName)"
    }
    foreach ($bank in $document.SoundBanksInfo.SoundBanks) {
        if ($bank.Language -ne 'SFX' -and $bank.Language -ne $Language) { continue }
        if ($banks.Contains($bank.Path)) { continue }
        $banks[$bank.Path] = $bank
    }
}
if (-not $banks.Contains('Init.bnk')) { throw '缺少 Init.json，请在 Wwise SoundBanks 设置中生成 JSON 元数据。' }

foreach ($bank in $banks.Values) {
    $dependencies = New-Object 'System.Collections.Generic.List[string]'
    foreach ($media in $bank.Media) {
        if ($media.Location -ne 'Loose') { continue }
        $mediaKey = 'Media:' + $media.Path
        if (-not $groups.Contains($mediaKey)) {
            $groups[$mediaKey] = [ordered]@{
                Key = $mediaKey; Files = @(New-ContentFile $media.Path)
                Banks = @(); PreparedEvents = @(); Dependencies = @()
            }
        }
        if (-not $dependencies.Contains($mediaKey)) { $dependencies.Add($mediaKey) }
    }
    $bankType = switch ($bank.Type) { 'User' { 0 }; 'Event' { 1 }; 'Bus' { 2 }; default { throw '未知 Bank 类型。' } }
    $groupKey = 'Bank:' + $bank.Path
    $preparedEvents = @()
    if ($bank.Type -eq 'Event') { $preparedEvents = @($bank.Events | ForEach-Object { $_.Name }) }
    $group = [ordered]@{
        Key = $groupKey; Files = @(New-ContentFile $bank.Path)
        Banks = @([ordered]@{ Name = $bank.ShortName; Path = $bank.Path; Type = $bankType })
        PreparedEvents = $preparedEvents; Dependencies = @($dependencies.ToArray())
    }
    if ($bank.ShortName -eq 'Init') { $init = $group; $init.Key = 'Init'; continue }
    $groups[$groupKey] = $group
    foreach ($audioEvent in $bank.Events) {
        if ($events.Contains($audioEvent.Name)) { throw "Event 重复，请检查语言或 Bank 制作策略：$($audioEvent.Name)" }
        $events[$audioEvent.Name] = [ordered]@{ Key = $audioEvent.Name; Groups = @($groupKey) }
    }
}

# 保持文件名和语言目录；地址使用现有 AddressByResPath，保留框架层的实现隔离。
foreach ($group in $groups.Values) {
    foreach ($file in $group.Files) {
        # Editor 以绝对制作目录读取，Player 由资源地址读取，因此 Location 由运行时决定。
        $file.Location = ''
    }
}
$versionText = 'AlloyAudioManifest-v1;172;' + $backendVersion + ';' + $Platform + ';' + $Language
foreach ($path in ($allFiles.Keys | Sort-Object)) { $versionText += ';' + $path + ':' + $allFiles[$path] }
$sha = [Security.Cryptography.SHA256]::Create()
try { $version = [BitConverter]::ToString($sha.ComputeHash($utf8.GetBytes($versionText))).Replace('-', '') }
finally { $sha.Dispose() }
$manifest = [ordered]@{
    Platform = $Platform; Language = $Language; BackendVersion = $backendVersion; Version = $version; Init = $init
    Groups = @($groups.Values); Events = @($events.Values)
}
$json = $manifest | ConvertTo-Json -Depth 30
[IO.File]::WriteAllText((Join-Path $sourceRoot 'AlloyAudioManifest.json'), $json, $utf8)
New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
foreach ($path in $allFiles.Keys) {
    $destination = Join-Path $destinationRoot $path
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot $path) -Destination $destination -Force
}
[IO.File]::WriteAllText((Join-Path $destinationRoot 'AlloyAudioManifest.json'), $json, $utf8)
Write-Host "框架音频清单已导出：$($events.Count) 个事件，$($groups.Count) 个资源组"
Write-Host "RawFile 收集目录：$destinationRoot"
