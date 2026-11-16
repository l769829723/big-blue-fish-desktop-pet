# 蓝色大肥鱼 · 1.1.28

修复文字气泡的尖角有时画在气泡上边、方向反了的问题。此前按“气泡有没有被往下推离自然位置”推断角色位置，窗口高度的舍入误差会把自然位置算到零以下再被夹到 0，启动问候、关闭告别这类固定短句因此每次都会把尖角画到上边；鱼靠近屏幕顶部时也会误判。现在改为按气泡中心与角色可见身体中心的实际上下关系决定尖角所在边，气泡在角色上方时尖角保持在底边向下。

Windows 标准版下载 `BigBlueFish-v1.1.28-OCR-Setup.exe`，无 ACP 版下载 `BigBlueFish-v1.1.28-Safe-OCR-Setup.exe`；Ubuntu／Debian x64 标准版下载 `BigBlueFish-v1.1.28-Linux-x64.deb`，无 ACP 版下载 `BigBlueFish-v1.1.28-Safe-Linux-x64.deb`。文件名中的 `Safe` 对应无 ACP 版，各安装包均提供配套 `.sha256` 校验文件。已安装的用户可以在设置中检查更新直接升级，也可以手动运行新版安装器；Windows 上选择原安装目录即可保留模型与设置。

连接方式与适配列表见 [连接本机 CLI Harness](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.28/docs/ACP.md)，内部工具参数见 [内部工具参考](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.28/docs/MCP.md)，完整行为见 [功能规格](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.28/docs/AGENT_FUNCTIONAL_SPEC.md)，版本变化见 [更新日志](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.28/CHANGELOG.md)。
