# Age of Empires II: Definitive Edition — Build 185872 Unofficial Trainer (+11)

[中文说明](#中文说明) · [Introduce](#Introduce)

## 中文说明

> **非官方说明：** 风灵月影（FLiNG）官网目前列出的版本为 Build 169652，且我们在本机发现该版本未能适配游戏 Build 185872。本项目因此参考其修改器的界面布局与操作方式，独立制作了针对 Build 185872 的版本。它不是风灵月影官方作品，与风灵月影、Microsoft 或 World's Edge 均无隶属、合作或认可关系。请勿将本项目当作官方更新。 [风灵月影官网页面](https://flingtrainer.com/trainer/age-of-empires-ii-definitive-edition-trainer/)

这是一个**仅供单机使用的公开测试版**。只适配 `AoE2DE_s.exe` 文件版本 `101.103.54800.0`（游戏 Build 185872）；其他版本不保证工作。它直接修改运行中的游戏内存，不修改游戏安装目录。

### 下载和使用

1. 从 [Releases](https://github.com/KagurazakaNatsuki/AOE2-Trainer/releases) 下载 EXE。
   发布 EXE 的 SHA-256：`705D0F056D95B02D7DCBE21C5AE837DC15893D7132DACD3E6C693F6DC2174E86`。
2. 启动游戏，进入**单人战局**，再以管理员身份运行修改器。主菜单中只能检测进程，进入战局后才会显示“已连接 / 就绪”。
3. 木材、食物、黄金、石料分别输入数值并各自点击“应用”。其他功能也可用对应按钮或小键盘热键执行。
4. “瞬间研发 / 建造 / 生产”是**一次性操作**，只处理当前任务或队列；下次使用需再点一次。
5. 右上角可切换简体中文、繁體中文和 English。
6. 左下角“项目主页：GitHub”可打开本仓库。

| 热键 | 功能 |
| --- | --- |
| Num 1 / 2 / 3 / 4 | 分别修改木材、食物、黄金、石料 |
| Num 6 | 恢复己方单位与建筑生命值 |
| Num 7 | 瞬间研发（一次） |
| Num 8 | 瞬间建造（一次） |
| Num 9 | 瞬间生产单位（一次） |
| Num 0 | 将 AI 单位与建筑设为 1 血 |
| Num + | 最大人口上限设为 200 |
| Num - | 当前人口设为 0 |

游戏速度功能因不稳定已移除。仓库公开[主程序源码](AoE2DELatestTrainer.cs)、[构建脚本](Build.ps1)与[管理员权限清单](admin.manifest)，供审阅。发布页提供经过 .NET 混淆处理的 EXE；仓库不包含诊断工具、原版风灵月影修改器或游戏文件。

### 风险与限制

- **请先备份存档，风险自担。** 内存修改可能导致游戏闪退、异常数值或存档问题；本工具不提供稳定性保证。
- 仅限单机；请勿用于多人联机或受反作弊保护的环境。
- **游戏账号风险无法排除。** 官方说明作弊可能导致账号处罚，严重或重复违规可能被暂停聊天、多人服务甚至游戏访问；游戏也可能提示“Tampering Detected”。本项目没有获得单机修改器的官方豁免，不能保证只在单机使用就不会触发检测或处罚。请勿在联机、排位、赛事中运行；进入这些模式前关闭修改器。有关处罚和干扰检测，请阅读[官方封禁说明](https://support.ageofempires.com/hc/en-us/articles/360047397872-Age-of-Empires-II-Definitive-Edition-ban)及[官方 Tampering Detected 说明](https://support.ageofempires.com/hc/en-us/articles/28006568546708-Tampering-Detected-A-running-process-may-be-interfering-with-the-correct-operation-of-AoE2-DE)。
- 游戏更新后可能立即失效。若版本不符，修改器会拒绝连接。
- EXE 没有数字签名；不要为了运行它而关闭杀毒软件或系统防护。
- 图标与封面嵌入在发布 EXE 中，但图片源文件不上传。无图片时可按[构建说明](BUILDING.md)编译功能相同但无自定义图像的程序；因此仓库中的材料不足以逐字节重现发布 EXE。游戏名称、标志及相关美术归各自权利人所有。
- 这是测试版：此前功能曾在单人战局手动验证，但当前加固发布包尚未完成新一轮完整游戏内回归测试。

如需反馈，请在 Issue 中提供游戏 Build、修改器版本、复现步骤和错误现象；不要上传个人信息、存档或含敏感数据的日志。

## Introduce

> **Unofficial project:** The latest build listed on FLiNG's [official trainer page](https://flingtrainer.com/trainer/age-of-empires-ii-definitive-edition-trainer/) is Build 169652, which did not work with game Build 185872 in our local testing. This independently written trainer for Build 185872 takes inspiration from FLiNG's UI layout and workflow. It is **not** an official FLiNG release and is not affiliated with, endorsed by, or sponsored by FLiNG, Microsoft, or World's Edge.

This is a **public beta for single-player use only**. It targets `AoE2DE_s.exe` file version `101.103.54800.0` (game Build 185872). Other versions are unsupported. It modifies the running game's memory; it does not modify the game installation directory.

### Download and use

1. Download the EXE from [Releases](https://github.com/KagurazakaNatsuki/AOE2-Trainer/releases).
   Release EXE SHA-256: `705D0F056D95B02D7DCBE21C5AE837DC15893D7132DACD3E6C693F6DC2174E86`.
2. Start the game and enter a **single-player match**, then run the trainer as administrator. Process detection in the main menu is normal; the trainer becomes ready after entering a match.
3. Enter and apply wood, food, gold, and stone amounts **separately**. Other features have individual buttons and numpad hotkeys.
4. Instant research, building, and recruitment are **one-shot actions** on current tasks or queues. Click again for later tasks.
5. Switch between Simplified Chinese, Traditional Chinese, and English in the top-right corner.
6. Use the bottom-left “Project page: GitHub” link to open this repository.

| Hotkey | Feature |
| --- | --- |
| Num 1 / 2 / 3 / 4 | Edit wood, food, gold, and stone separately |
| Num 6 | Restore health of your units and buildings |
| Num 7 | Instant research (once) |
| Num 8 | Instant buildings (once) |
| Num 9 | Instant recruit units (once) |
| Num 0 | Set AI units and buildings to 1 HP |
| Num + | Set maximum population cap to 200 |
| Num - | Set current population to 0 |

The game-speed option was removed because it was unstable. The repository includes the [main source](AoE2DELatestTrainer.cs), [build script](Build.ps1), and [administrator manifest](admin.manifest) for review. The Release EXE is .NET-obfuscated. Diagnostic tools, the original FLiNG trainer, and game files are not included.

### Risks and limitations

- **Back up saves and use at your own risk.** Memory editing may cause crashes, abnormal values, or save problems. No stability warranty is provided.
- Single-player only; do not use in multiplayer or anti-cheat-protected environments.
- **Account safety is not guaranteed.** The game publisher says cheating can result in account sanctions, potentially including loss of chat, multiplayer, or game access for severe or repeated violations. The game may also show a “Tampering Detected” warning. We have no official exemption for single-player trainers and cannot guarantee that single-player use is undetected or free of account consequences. Never run the trainer in multiplayer, ranked play, or tournaments; close it before entering those modes. See the [official ban guidance](https://support.ageofempires.com/hc/en-us/articles/360047397872-Age-of-Empires-II-Definitive-Edition-ban) and [tampering guidance](https://support.ageofempires.com/hc/en-us/articles/28006568546708-Tampering-Detected-A-running-process-may-be-interfering-with-the-correct-operation-of-AoE2-DE).
- A game update may break compatibility immediately. The trainer refuses to attach when the executable file version does not match.
- The EXE is unsigned. Do not disable antivirus or system protections to run it.
- The icon and cover art remain embedded in the Release EXE, but the image source files are not uploaded. [Build instructions](BUILDING.md) can produce a functional image-free build; the public repository therefore cannot reproduce the exact Release EXE byte for byte. Game names, logos, and artwork belong to their respective owners.
- This is a beta: features were manually checked in an earlier single-player build, but the current protected package has not yet received a full in-game regression test.

For bug reports, open an Issue with the game build, trainer version, reproduction steps, and symptoms. Do not post personal data, save files, or sensitive logs.
