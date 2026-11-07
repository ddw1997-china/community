#Requires -Version 5.1
<#
.SYNOPSIS
    按原参数构建 InkCanvasForClass 本地手工发布包（x86 / 自包含 / 单文件 / 不裁剪）。

.DESCRIPTION
    1. 可选更新 version.json 的版本号；
    2. 必要时构建 PowerPoint VSTO 加载项（ppt-agent 的来源）；
    3. dotnet restore --locked-mode + dotnet publish（固定原参数）；
    4. 校验版本信息、启动冒烟、清理运行期文件；
    5. 打包成 artifacts\InkCanvasForClass.CE.<版本>.zip 并输出 SHA256；
    6. 加 -Publish 时：提交源码 → 推送 → 创建 tag → 用本地包创建/覆盖 GitHub Release。

.EXAMPLE
    .\build-release.ps1 -Version 1.8.1.1108

.EXAMPLE
    .\build-release.ps1 -Version 1.8.1.1108 -Publish -CommitMessage "fix: ..." -NotesFile .\notes.md
#>
[CmdletBinding()]
param(
    # 四段版本号，例如 1.8.1.1108；省略则沿用 version.json 中的现值
    [string]$Version,

    # 发布输出目录（相对仓库根目录）
    [string]$OutputDir = 'artifacts/publish/x86-clean2',

    # 强制重新构建 PowerPoint VSTO 加载项
    [switch]$RebuildAddIn,

    # 跳过启动冒烟测试
    [switch]$SkipSmokeTest,

    # 可复现构建：源码路径归一 + 确定性编译，配合同一 commit / 同一 SDK 可产出逐字节相同的包
    [switch]$Deterministic,

    # 构建完成后推源码、打 tag 并发布 GitHub Release
    [switch]$Publish,

    # -Publish 时若有未提交改动，用这条信息提交
    [string]$CommitMessage,

    # 本机未配置 git 身份时，-Publish 提交可临时指定（不写入 git config）
    [string]$GitName,
    [string]$GitEmail,

    # Release 说明文件（Markdown）；省略则自动用最近一次提交信息
    [string]$NotesFile
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Invoke-Native([string]$File, [string[]]$Arguments, [string]$ErrorText) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$ErrorText（退出码 $LASTEXITCODE）" }
}

$root = $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }
Set-Location $root

# ---------- 0. 版本号 ----------
$versionFile = Join-Path $root 'version.json'
if (-not (Test-Path $versionFile)) { throw "找不到 version.json：$versionFile" }

if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        throw "版本号必须是四段数字，例如 1.8.1.1108（当前传入：$Version）"
    }
    Write-Step "写入版本号 $Version"
    $json = Get-Content $versionFile -Raw
    $json = $json -replace '"version"\s*:\s*"[^"]+"', ('"version": "' + $Version + '"')
    [IO.File]::WriteAllText($versionFile, $json, (New-Object System.Text.UTF8Encoding($false)))
}

$currentVersion = ([regex]::Match((Get-Content $versionFile -Raw), '"version"\s*:\s*"([^"]+)"')).Groups[1].Value
if (-not $currentVersion) { throw "无法从 version.json 解析版本号" }
Write-Host "版本：$currentVersion"

# 版本号来自 git（Nerdbank.GitVersioning），缺 .git 会丢掉短 SHA 后缀
if (-not (Test-Path (Join-Path $root '.git'))) {
    Write-Warning '仓库根目录没有 .git，程序集信息版本将不含短 SHA，版本号也可能与预期不同'
}

# ---------- 1. -Publish 的提交 ----------
$branch = (& git rev-parse --abbrev-ref HEAD).Trim()
if ($Publish) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw '未找到 gh CLI，无法发布 Release' }

    $gitIdentity = @()
    if (-not (git config user.email)) {
        if ($GitName -and $GitEmail) {
            # 用 -c 临时身份提交，不改动 git config
            $gitIdentity = @('-c', "user.name=$GitName", '-c', "user.email=$GitEmail")
        }
        else {
            throw '未配置 git user.email，请配置 git 身份，或用 -GitName / -GitEmail 传入提交身份'
        }
    }

    $dirty = @(git status --porcelain)
    if ($dirty.Count -gt 0) {
        if (-not $CommitMessage) {
            throw "工作区有未提交改动，请先提交，或用 -CommitMessage 让脚本一并提交：`n$($dirty -join "`n")"
        }
        Write-Step "提交源码：$CommitMessage"
        Invoke-Native git @('add', '-A') 'git add 失败'
        Invoke-Native git (@($gitIdentity) + @('commit', '-m', $CommitMessage)) 'git commit 失败'
    }
}

# ---------- 2. PowerPoint VSTO 加载项 ----------
$addInDir = Join-Path $root 'InkCanvas.PowerPointAddIn\bin\Release'
$addInMarker = Join-Path $addInDir 'Microsoft.Office.Tools.Common.v4.0.Utilities.dll'

if ($RebuildAddIn -or -not (Test-Path $addInMarker)) {
    Write-Step '构建 PowerPoint VSTO 加载项（ppt-agent 来源）'

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = $null
    if (Test-Path $vswhere) {
        $msbuild = (& $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)
    }
    if (-not $msbuild) { $msbuild = (Get-Command msbuild -ErrorAction SilentlyContinue).Source }
    if (-not $msbuild) { throw '未找到 MSBuild，请安装 Visual Studio Build Tools（含 .NET 桌面/VSTO 组件）' }

    Invoke-Native $msbuild @(
        'InkCanvas.PowerPointAddIn\InkCanvas.PowerPointAddIn.csproj',
        '/p:Configuration=Release',
        '/verbosity:minimal',
        '-maxcpucount'
    ) '构建 VSTO 加载项失败'

    if (-not (Test-Path $addInMarker)) { throw "加载项输出缺少 $addInMarker，ppt-agent 会不完整" }
}
else {
    Write-Host '复用已有 VSTO 加载项输出（-RebuildAddIn 可强制重建）'
}

# ---------- 3. 还原 + 发布（原参数） ----------
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }

Write-Step '锁定模式还原'
Invoke-Native dotnet @('restore', 'Ink Canvas/InkCanvasForClass.csproj', '--locked-mode', '-p:Platform=x86') '还原失败'

Write-Step 'publish（Release / win-x86 / 自包含 / 单文件 / 不裁剪）'
$publishArgs = @(
    'publish', 'Ink Canvas/InkCanvasForClass.csproj',
    '--configuration', 'Release',
    '--no-restore',
    '--runtime', 'win-x86',
    '--self-contained', 'true',
    '--output', $OutputDir,
    '-p:Platform=x86',
    '-p:PublishSingleFile=true',
    '-p:PublishTrimmed=false',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:UseMultiToolTask=true',
    '-p:EnforceProcessCountAcrossBuilds=true',
    '-p:RunAnalyzers=false'
)
if ($Deterministic) {
    # 副本不在同一路径时，嵌入的 PDB 会记录不同源码路径，导致 exe 不同；
    # PathMap 把仓库根映射成固定虚拟路径，ContinuousIntegrationBuild 打开确定性编译。
    $publishArgs += @('-p:ContinuousIntegrationBuild=true', "-p:PathMap=$root=/_/")
}
Invoke-Native dotnet $publishArgs '发布失败'

# ---------- 4. 校验 ----------
Write-Step '校验产物'
$exe = Join-Path $OutputDir 'InkCanvasForClass.exe'
if (-not (Test-Path $exe)) { throw "缺少主程序：$exe" }

$pptAgent = Join-Path $OutputDir 'ppt-agent'
if (-not (Test-Path (Join-Path $pptAgent 'Microsoft.Office.Tools.Common.v4.0.Utilities.dll'))) {
    Write-Warning "ppt-agent 不完整（缺少 VSTO 依赖 DLL），请检查加载项构建"
}

$info = (Get-Item $exe).VersionInfo
Write-Host ("FileVersion    : {0}" -f $info.FileVersion)
Write-Host ("ProductVersion : {0}" -f $info.ProductVersion)
if ($info.FileVersion -ne $currentVersion) {
    throw "FileVersion ($($info.FileVersion)) 与 version.json ($currentVersion) 不一致"
}

# ---------- 5. 冒烟 ----------
if (-not $SkipSmokeTest) {
    Write-Step '启动冒烟（15 秒）'
    $proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
    Start-Sleep -Seconds 15
    $alive = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    if (-not $alive) { throw "程序启动后退出，退出码 $($proc.ExitCode)" }
    Write-Host ("运行中，内存 {0} MB" -f [math]::Round($alive.WorkingSet64 / 1MB))
    Stop-Process -Id $proc.Id -Force
    # 进程退出后文件句柄才释放，等一等再做清理
    $proc | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# ---------- 6. 清理成纯发布态 ----------
Write-Step '清理运行期文件'
$runtimeItems = @(
    'Automations', 'Backups', 'Configs', 'Logs',
    'PluginLogs', 'PluginConfigs', 'PluginPackages', 'Plugins',
    'Crashes', 'Saves', 'device_id.dat', 'usage_stats.enc',
    'IACore.dll', 'IALoader.dll', 'IAWinFX.dll'
)
foreach ($item in $runtimeItems) {
    # 刚退出的进程可能仍短暂占用文件，重试几次避免静默留下运行期文件
    $path = Join-Path $OutputDir $item
    for ($i = 0; $i -lt 10 -and (Test-Path $path); $i++) {
        Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $path) { Start-Sleep -Milliseconds 300 }
    }
    if (Test-Path $path) { Write-Warning "未能删除运行期文件：$item" }
}
# 引用项目被重新编译时会带出 pdb，发布包里不需要
Get-ChildItem $OutputDir -Recurse -Filter *.pdb -File -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
$fileCount = (Get-ChildItem $OutputDir -Recurse -File).Count
Write-Host "发布目录文件数：$fileCount"

# ---------- 7. 打包 ----------
Write-Step '打包本地手工包'
$zip = Join-Path $root ("artifacts\InkCanvasForClass.CE.{0}.zip" -f $currentVersion)
if (Test-Path $zip) { Remove-Item $zip -Force }

# 用固定时间戳、固定条目顺序写 zip：Compress-Archive 会带入文件修改时间，
# 同一份内容每次打包的 SHA256 都不同，副本之间也就无法比对"是否同一个分发文件"。
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$baseDir = (Resolve-Path $OutputDir).Path.TrimEnd('\')
$fixedTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$fileStream = [IO.File]::Open($zip, [IO.FileMode]::Create)
try {
    $archive = New-Object IO.Compression.ZipArchive($fileStream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $baseDir -Recurse -File |
            Sort-Object { $_.FullName.Substring($baseDir.Length) } |
            ForEach-Object {
                $entryName = $_.FullName.Substring($baseDir.Length + 1).Replace('\', '/')
                $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTime
                $input = [IO.File]::OpenRead($_.FullName)
                try {
                    $output = $entry.Open()
                    try { $input.CopyTo($output) } finally { $output.Dispose() }
                }
                finally { $input.Dispose() }
            }
    }
    finally { $archive.Dispose() }
}
finally { $fileStream.Dispose() }

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host ("包：{0}" -f $zip)
Write-Host ("大小：{0} MB" -f [math]::Round((Get-Item $zip).Length / 1MB, 1))
Write-Host ("SHA256：{0}" -f $hash)

# ---------- 8. 推送源码 + 发布 Release ----------
if ($Publish) {
    Write-Step "推送源码到 origin/$branch"
    Invoke-Native git @('push', 'origin', $branch) 'git push 失败'

    $tag = $currentVersion
    $tagCommit = (& git rev-parse -q --verify "refs/tags/$tag")
    if ($tagCommit) {
        $headCommit = (& git rev-parse HEAD).Trim()
        if ($tagCommit.Trim() -ne $headCommit) {
            throw "tag $tag 已存在且指向 $($tagCommit.Trim())，与 HEAD $headCommit 不同；脚本不会强制移动已发布标签"
        }
        Write-Host "tag $tag 已存在并指向 HEAD"
    }
    else {
        Write-Step "创建并推送 tag $tag"
        Invoke-Native git @('tag', $tag) 'git tag 失败'
        Invoke-Native git @('push', 'origin', $tag) '推送 tag 失败'
    }

    Write-Step "发布 GitHub Release $tag（仅上传本地包）"
    if (-not $NotesFile) {
        $NotesFile = Join-Path $env:TEMP ("icc-release-notes-{0}.md" -f $tag)
        $subject = (& git log -1 --pretty=%s)
        $body = "## 更新内容`n`n- $subject`n`n## 安装说明`n`n下载压缩包后解压即可运行，无需安装 .NET 运行时（已内嵌 .NET 10 x86）。`n`n``ppt-agent`` 为 PowerPoint 加载项，使用前需安装 VSTO 4.0 Runtime。"
        Set-Content -Path $NotesFile -Value $body -Encoding UTF8
    }
    if (-not (Test-Path $NotesFile)) { throw "说明文件不存在：$NotesFile" }

    # PS 5.1 在 $ErrorActionPreference='Stop' 下会把原生命令的 stderr 当异常抛，
    # 而 "release not found" 走 stderr，这里临时放宽并只看退出码
    $releaseExists = $false
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & gh release view $tag --json tagName 2>$null | Out-Null
        $releaseExists = ($LASTEXITCODE -eq 0)
    }
    catch {
        $releaseExists = $false
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($releaseExists) {
        Invoke-Native gh @('release', 'upload', $tag, $zip, '--clobber') '覆盖 Release 资产失败'
        Invoke-Native gh @('release', 'edit', $tag, '--latest', '--notes-file', $NotesFile) '更新 Release 说明失败'
    }
    else {
        Invoke-Native gh @(
            'release', 'create', $tag, $zip,
            '--title', "ICC CE $tag",
            '--latest',
            '--notes-file', $NotesFile
        ) '创建 Release 失败'
    }
}

Write-Step '完成'
Write-Host "版本 $currentVersion 构建完成：$zip"
