# Building / 构建

## 中文

公开的 C# 源码用于审阅修改器逻辑。Windows x64 且安装 .NET Framework 4.x 的机器可在 PowerShell 中运行 `./Build.ps1`，输出位于 `build/AoE2DE.Plus12.exe`。脚本只调用系统自带的 C# 编译器，不下载依赖。

仓库**不包含**图标及封面素材。默认构建仍可运行，但使用系统默认图标且左侧封面留空。若你拥有可用的 ICO 和 PNG，可用 `./Build.ps1 -IconPath <绝对ICO路径> -CoverPath <绝对PNG路径>` 嵌入自己的素材。

GitHub Release 中的 EXE 使用了额外图像素材并经过 Obfuscar 2.2.50 混淆；由于素材没有公开，公开源码无法逐字节复现该 EXE。请根据 README 的 SHA-256 校验发布文件。公开源码不包含原版 FLiNG 程序、游戏文件、诊断内存转储或扫描结果。

## English

The published C# source is provided for inspecting the trainer logic. On Windows x64 with .NET Framework 4.x, run `./Build.ps1` in PowerShell. It writes `build/AoE2DE.Plus12.exe` and only invokes the local C# compiler; it downloads no dependencies.

The repository does **not** include the icon or cover art. The default build remains functional but uses the system icon and has a blank cover panel. If you have your own ICO and PNG, run `./Build.ps1 -IconPath <absolute ICO path> -CoverPath <absolute PNG path>` to embed them.

The GitHub Release EXE includes separate artwork and was obfuscated with Obfuscar 2.2.50. As that artwork is not public, the repository cannot reproduce the Release EXE byte for byte. Verify the distributed file against the SHA-256 in the README. The published source excludes the original FLiNG binary, game files, memory dumps, and scan results.
