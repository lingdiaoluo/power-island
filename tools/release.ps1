<#
.SYNOPSIS
  一键发版：编译 → 建 tag → 创建 GitHub Release → 上传 exe。

.EXAMPLE
  $env:GITHUB_TOKEN = "ghp_xxx"        # 只需一次（建议加到用户环境变量里）
  .\tools\release.ps1 -Version 1.0.1

.EXAMPLE
  # 自定义更新说明、先存草稿
  .\tools\release.ps1 -Version 1.1.0 -Notes "新增 XXX 支持" -Draft
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Notes = "",

    [string]$Owner = "lingdiaoluo",
    [string]$Repo = "power-island",

    [switch]$Draft
)

$ErrorActionPreference = "Stop"

# ---------- 前置检查 ----------
$token = $env:GITHUB_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Host "缺少 GITHUB_TOKEN 环境变量。" -ForegroundColor Red
    Write-Host "在 https://github.com/settings/tokens 生成一个带 repo 权限的 token，然后：" -ForegroundColor Yellow
    Write-Host '  $env:GITHUB_TOKEN = "ghp_xxx"' -ForegroundColor Yellow
    exit 1
}

$Version = $Version.TrimStart("v")
$tag = "v$Version"

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot "PowerIsland\PowerIsland.csproj"))) {
    $repoRoot = $PSScriptRoot
}
$proj = Join-Path $repoRoot "PowerIsland\PowerIsland.csproj"
$dist = Join-Path $repoRoot "dist"

foreach ($cmd in @("git", "dotnet", "curl")) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        Write-Host "找不到命令：$cmd" -ForegroundColor Red
        exit 1
    }
}

$existing = git -C $repoRoot tag --list $tag
if ($existing) {
    Write-Host "tag $tag 已存在，请换一个版本号。" -ForegroundColor Red
    exit 1
}

# ---------- 编译 ----------
Write-Host "==> 编译 v$Version" -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=none `
    -p:Version=$Version `
    -o $dist
if ($LASTEXITCODE -ne 0) { Write-Host "编译失败" -ForegroundColor Red; exit 1 }

$exe = Join-Path $dist "PowerIsland.exe"
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$sha = (Get-FileHash $exe -Algorithm SHA256).Hash
Write-Host "    $exe  ($sizeMb MB)" -ForegroundColor DarkGray
Write-Host "    SHA256 $sha" -ForegroundColor DarkGray

# ---------- tag ----------
Write-Host "==> 打 tag $tag" -ForegroundColor Cyan
git -C $repoRoot tag -a $tag -m "v$Version"
git -C $repoRoot push origin $tag
if ($LASTEXITCODE -ne 0) { Write-Host "推送 tag 失败（先 git push 提交？）" -ForegroundColor Red; exit 1 }

# ---------- Release ----------
if ([string]::IsNullOrWhiteSpace($Notes)) {
    $lastTag = git -C $repoRoot describe --tags --abbrev=0 "$tag^" 2>$null
    $range = if ($lastTag) { "$lastTag..HEAD" } else { "HEAD" }
    $log = git -C $repoRoot log $range --pretty=format:"- %s" 2>$null
    $Notes = if ($log) { "**本次变更**`n`n$log" } else { "发布 v$Version" }
}

$body = @{
    tag_name         = $tag
    target_commitish = "main"
    name             = "v$Version"
    body             = "$Notes`n`n---`n`n文件：``PowerIsland.exe``（$sizeMb MB）`nSHA-256：``$sha``"
    draft            = [bool]$Draft
    prerelease       = $false
} | ConvertTo-Json -Depth 5

$headers = @{
    Authorization          = "Bearer $token"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}

Write-Host "==> 创建 Release $tag" -ForegroundColor Cyan
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Owner/$Repo/releases" `
    -Method Post -Headers $headers -Body $body -ContentType "application/json"

Write-Host "==> 上传 asset（$sizeMb MB，请稍候）" -ForegroundColor Cyan
$uploadUrl = ($release.upload_url -replace "\{\?name,label\}", "") +
             "?name=PowerIsland.exe&label=Windows%20x64"
& curl.exe -sS -X POST `
    -H "Authorization: Bearer $token" `
    -H "Accept: application/vnd.github+json" `
    -H "Content-Type: application/octet-stream" `
    --data-binary "@$exe" `
    $uploadUrl | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "上传失败，可到网页手动补传：$($release.html_url)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "完成：$($release.html_url)" -ForegroundColor Green
Write-Host "下载直链：https://github.com/$Owner/$Repo/releases/download/$tag/PowerIsland.exe" -ForegroundColor Green
Write-Host "SHA-256：$sha" -ForegroundColor DarkGray
