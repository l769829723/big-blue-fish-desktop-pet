# 蓝色大肥鱼 · 1.1.31

鱼鱼现在会自己遛弯了。空闲时她会从桌面上站起来走一小段，遇到合适的真实窗口，还会沿着窗边一路爬到上沿，趴在窗口顶上哼歌、发呆，甚至打个盹；窗口被拖动或改变大小时她会跟着一起挪动，窗口关掉后她会“哎呀”一下落回桌面。遛弯只在鼠标键盘安静至少 5 秒后开始，隐藏、全屏、拖动、打开设置或进入 Agent 接管时会立刻停下，设置里可以随时关闭。所有动作复用现有的 106 段动画。

这一版还修复了模型较慢时重复请求下一句的问题：此前下一次自动说话从开始请求时就算起，模型生成时间超过说话间隔时，间隔会在这一句还没显示完时就到期，气泡还在逐字显示就又转起等待动画并请求新文本。现在下一次自动说话从生成结束、且当前气泡完整显示之后才开始计时，气泡逐字显示期间不会启动新的请求；外部 MCP 或 Agent 送来的原文同样等整句显示完再恢复计时，Windows 与 Linux 两版行为一致。

Windows 标准版下载 `BigBlueFish-v1.1.31-OCR-Setup.exe`，无 ACP 版下载 `BigBlueFish-v1.1.31-Safe-OCR-Setup.exe`；Ubuntu／Debian x64 标准版下载 `BigBlueFish-v1.1.31-Linux-x64.deb`，无 ACP 版下载 `BigBlueFish-v1.1.31-Safe-Linux-x64.deb`。文件名中的 `Safe` 对应无 ACP 版，各安装包均提供配套 `.sha256` 校验文件。已安装的用户可以在设置中检查更新直接升级，也可以手动运行新版安装器；Windows 上选择原安装目录即可保留模型与设置。

连接方式与适配列表见 [连接本机 CLI Harness](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.31/docs/ACP.md)，内部工具参数见 [内部工具参考](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.31/docs/MCP.md)，完整行为见 [功能规格](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.31/docs/AGENT_FUNCTIONAL_SPEC.md)，版本变化见 [更新日志](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.31/CHANGELOG.md)。
