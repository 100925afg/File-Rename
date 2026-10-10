# 重新编译「批量文件重命名工具」
#
# 环境说明：本机 DSH 沙箱禁止写 C:\Users\<用户>\AppData，导致 `dotnet build`（NuGet 恢复）
# 无法运行。因此改用 SDK 自带的 Roslyn 编译器直接编译，不需要 NuGet 包。
# 如果你在自己机器上能正常联网/有写权限，直接用 `dotnet build FileRenamer\FileRenamer.csproj -c Release` 也可以。

$ErrorActionPreference = 'Stop'

$root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $root) { $root = (Get-Location).Path }
$root = (Get-Item -LiteralPath $root).FullName

# dotnet 需要可写的 HOME，这里指到工作区内的临时目录，避开沙箱限制
$env:DOTNET_CLI_HOME = Join-Path $root '.dsh-dotnet-home'
$env:DOTNET_NOLOGO   = '1'
New-Item -ItemType Directory -Force -Path $env:DOTNET_CLI_HOME | Out-Null

$csc = 'C:\Program Files\dotnet\sdk\6.0.423\Roslyn\bincore\csc.dll'
$fx  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'   # .NET Framework 4.8 运行时程序集

if (-not (Test-Path -LiteralPath $csc)) { throw "找不到 Roslyn 编译器：$csc" }
if (-not (Test-Path -LiteralPath $fx))  { throw "找不到 .NET Framework 目录：$fx" }

$refs = @(
    "/reference:$fx\mscorlib.dll",
    "/reference:$fx\System.dll",
    "/reference:$fx\System.Core.dll",
    "/reference:$fx\System.Drawing.dll",
    "/reference:$fx\System.Windows.Forms.dll"
)

$outExe = Join-Path $root 'FileRenamer.exe'

$args = @(
    'exec', $csc,
    '/noconfig', '/nostdlib+', '/nologo',
    '/target:winexe', '/platform:anycpu', '/optimize+',
    '/codepage:65001', '/langversion:7.3', '/warn:4',
    "/out:$outExe",
    "/win32manifest:$root\FileRenamer\app.manifest"
) + $refs + @(
    "$root\FileRenamer\Program.cs",
    "$root\FileRenamer\MainForm.cs"
)

Write-Host '正在编译…'
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

# 同步一份到项目约定输出目录，方便对比/调试
$binDir = Join-Path $root 'FileRenamer\bin'
New-Item -ItemType Directory -Force -Path $binDir | Out-Null
Copy-Item -LiteralPath $outExe -Destination (Join-Path $binDir 'FileRenamer.exe') -Force

$f = Get-Item -LiteralPath $outExe
Write-Host ''
Write-Host ("编译成功：{0}  ({1} 字节, {2})" -f $f.FullName, $f.Length, $f.LastWriteTime)
