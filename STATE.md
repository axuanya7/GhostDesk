# STATE —— 唯一事实源

最后更新：2026-11-24 13:59 by 定时任务与 STATE（本会话）

> 规矩：动任何文件之前先读这份；要走的时候把这份补上。多个会话同时开的时候，靠下面的「占用」和「会话登记」两张表不打架。

## 现在在做什么

定时启动的「一次性」改造已完成并编译运行：保存时按当前时间自动判定今天/明天（时间已过就明天），并新增「执行日期」自选日历（存 `OnceDate`，空 = 自动）。`bin\GhostDesk.exe` 已重建（2026-11-24 13:58，734720 字节），旧进程 8492 已结束、新进程 4560 在跑，**等用户点开设置页验收**。Git 仍未提交。

## 下次从哪继续

- 上次停在哪：一次性日期改造写完、编译通过、新 exe 已启动（进程 4560），未提交 Git。
- 下次先执行什么：等用户验收设置页 → 定时启动，确认「执行日期」补丁与日历弹层的样子；要发包就把 `src\AppInfo.cs` 的 `Version` 改成新号，再 `.\build.ps1`，最后 `.\build-installer.ps1`。
- 不预期换哪条路：不引入 `.csproj` / MSBuild，继续用 `build.ps1` 里的 `csc.exe` 单条命令编译；不把 `src` 拆成多项目；不改成「搬动用户文件」的收纳方式（见「已知的坑」第一条）。

## 占用（同一块地只许一个会话动，改完就删这一整行）

| 范围 | 谁 | 开始 | 在改什么 |
|---|---|---|---|

## 会话登记（谁开着就在这填一行，关掉就划掉；没登记的不算在跑）

| 会话 | 管什么 | 最后活动 | 备注 |
|---|---|---|---|
| 定时任务与 STATE（本会话） | `src\ScheduledTask*.cs`、`AppConfig.cs`、`STATE.md` | 2026-11-24 13:59 | 一次性日期改造 + 新建本文件；未提交 Git |

「未登记等于没在跑」这条是有意的：我看不到别人的会话，唯一能确认的方式就是它在上面留过一行。

## 待办

- 等用户验收「执行日期」（一次性）：底部 `自动（今天 / 明天）` 与实际今天/明天是否对得上，日历弹层能不能选到想要的日期。
- 验收通过后再决定要不要发 `v1.4.7`（改 `src\AppInfo.cs` → `.\build.ps1` → `.\build-installer.ps1`）。
- 验收通过后提交 Git（`STATE.md` 是新增文件，别 `git add -A` 带上 `.backups/`）。
- 旧的过期一次性任务（设置页里「抢刀 · 2026-11-01 11:55」那条）需要重新打开保存一次才会滚到新日期，保存后仍是「已暂停」，要手动点「启用」。

## 最近改动

- 2026-11-24 13:58：定时启动「一次性」改造。`ScheduledTaskConfig` 新增 `OnceDate`（`yyyy-MM-dd`，空 = 自动）；`ScheduledTaskWindow` 增加「执行日期」行 + 自选日历，一次性任务**每次保存都重算**首次执行时间；新增 `src\DatePickerControl.cs`；`ScheduledTaskRunner.CalculateFirstRunUtc` 增加带日期的重载与 `TryParseOnceDate`；`AppConfig` 增加该字段的序列化与规范化。编译 0 错误，`bin\GhostDesk.exe` 已更新。
- 2026-11-24：新增本文件 `STATE.md`（多会话协作规范）。
- 2026-10-29 20:17：`README.md` 更新（v1.4.6 下载链接与说明）。
- 2026-10-21 19:43：`README.md` 备份为 `README.md.20261021-194300.bak`；同批留下 `src\AppInfo.cs.20261021-194300.bak`、`src\GlassPromptWindow.cs.20261021-193605.bak`、`src\RegionControl.cs.20261021-193605.bak`、`src\RegionControl.cs.bak-20261021183813`、`src\RegionControl.cs.bak-20261021192516`。

## 已知的坑（踩过就别再踩）

1. **收纳绝不搬动文件**。桌面项只加「隐藏」属性、桌面外项只生成同名 `.lnk`、分区里只存引用。README 是对用户承诺过的，改收纳逻辑时不能改成移动/复制/删除源文件；退出时恢复图标也只是移除引用 + 还原可见性。
2. **没有 `.csproj`，编译靠 `build.ps1` 里的 `csc.exe`**（`.NET Framework 4.0.30319` 下的 `csc.exe`，`/target:winexe`）。加新 `.cs` 不用登记进工程，`Get-ChildItem -Filter '*.cs'` 通吃；但图标和捐赠图是 `/resource:` 硬编码路径，换文件名就得改脚本。
3. **版本号只有一个真源**：`src\AppInfo.cs` 里的 `Version = "x.y.z"`。`build-installer.ps1` 就是拿正则从这里读，读不到直接抛「无法从 AppInfo.cs 中读取版本号」。
4. **本文件所在的根目录有 25 MB 的 `debug.log`**（`26079652` 字节，2026-10-19）。它被 `*.log` 忽略、不会进 Git，别顺手删；要清就先备份。
5. **`.backups/` 不在 `.gitignore` 里**，所以 `git status` 永远显示 `?? .backups/`（14 个文件）。别图省事 `git add -A` 把备份包一起提上去，提交时按路径点名。
6. **`docs/` 和 `qa/` 是被忽略的**（`.gitignore` 里 `docs/` 在末尾，`qa/` 在中间），所以那两个目录的文档和截图只在本机，`git pull` 到别的机器不会有。要长期留的证据得挪进已跟踪的路径。
7. **`bin/`、`dist/`、`.testdata/` 也全忽略**，`dist` 里的安装包只靠 GitHub Releases 留存，本机删了就没了。
8. `src` 下散着 5 个 `.bak` / `.20261021-*.bak` 备份（`RegionControl.cs` 占 3 个）。动 `RegionControl.cs`（169 KB）、`SettingsWindow.cs`（130 KB）这类大文件前先留一份自己的备份，别覆盖掉这些老的。
9. 装包编译依赖 Inno Setup 6 的 `ISCC.exe`：先找 PATH，找不到退到 `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`，都没有就报错——换机器先装它。
10. **一次性任务的日期是「保存时重算」，不是存死的**：`OnceDate` 空 = 自动（这个时间今天还没过就今天，过了就明天），非空 = 用户自选的 `yyyy-MM-dd`（若选的那天该时间已过，同样顺延到次日）。`ScheduledTaskWindow.Confirm_Click` 对 `once` 模式**每次保存都调 `CalculateFirstRunUtc`**，别改回「只在计划签名变化时才重算」——那样过期任务会永远停在过去日期，`GetDueOccurrence` 的 2 分钟宽限之外永不触发。

## 关键路径与命令

根目录：`G:\01桌面工具\GhostDesk`

| 东西 | 位置 / 命令 |
|---|---|
| 源码（C# + WPF，30 个文件） | `src\` —— 主体 `RegionControl.cs`(169 KB)、`SettingsWindow.cs`(130 KB)、`DesktopWindow.cs`(101 KB)、`AppConfig.cs`(71 KB)、`ItemStorage.cs`(60 KB)、`Program.cs`(41 KB)、`ScheduledTaskWindow.cs`(36 KB)、`DatePickerControl.cs`(一次性日期选择) |
| 版本号 | `src\AppInfo.cs` 的 `Version = "x.y.z"` |
| 图标 / 捐赠图 | `assets\GhostDesk.ico`、`assets\donate-alipay.jpg`、`assets\donate-wechat.png` |
| 编译 exe | `.\build.ps1` → `bin\GhostDesk.exe` |
| 打安装包 + 便携 zip | `.\build-installer.ps1 -Version 1.4.7`（加 `-SkipZip` 只出安装包）→ `dist\GhostDesk-Setup-1.4.7.exe`、`dist\GhostDesk-v1.4.7-win-x64.zip` |
| 安装脚本 | `installer.iss`（Inno Setup，版本由 `/DAppVersion=` 传入） |
| 运行期配置与备份 | `%APPDATA%\GhostDesk\config.json`、`%APPDATA%\GhostDesk\backups` |
| 文档 | `README.md`（已跟踪）、`docs\`（被忽略：`兼容与升级约定.md`、`file-hide-port.md`、`CODEX-TASK-storage-safety.md`） |
| QA 证据 | `qa\`（被忽略：大量截图 + `FirstRunSmokeTest.exe`） |
| 仓库 | `github.com/axuanya7/GhostDesk` |
| 手工备份区 | `.backups\`（14 个文件，未跟踪）、根目录 `README.md.20261021-194300.bak` |

## 会话分工（同一时间同一块地只许一个会话）

| 会话 | 管什么 | 不碰什么 |
|---|---|---|
| 文档与 STATE | `STATE.md`、`README.md`、`docs\` | 不碰 `src`、不打包 |
| 分区与窗口 UI | `src\RegionControl.cs`、`DesktopWindow.cs`、`GlassTheme.cs`、`TrayMenuTheme.cs`、各类 Window | 不动 `Updater`/发布流程 |
| 设置与配置 | `src\SettingsWindow.cs`、`AppConfig.cs`、`ItemStorage.cs`、`StorageSafety.cs` | 不动收纳交互主循环 |
| 启动 / 定时任务 | `src\AppScanner.cs`、`ScheduledTask*.cs`、`AppPickerWindow.cs` | 不动设置页布局 |
| 更新与发布 | `src\UpdateChecker.cs`、`UpdateNotificationWindow.cs`、`Updater.cs`、`build*.ps1`、`installer.iss`、`dist\` | 不动业务代码 |
| QA | `qa\`、`.testdata\`、`tests\` | 只读代码，不改代码 |
