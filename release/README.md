# 蓝色大肥鱼 · 1.1.30

无ACP版也提供外部 MCP 接入。无ACP版仍然不包含 Agent 连接、ACP、内部工具（设置修改、截图、Agent 对话与预约）、双击输入框和 Agent 设置页面，也不分发 Agent 运行时；但它现在和标准版一样随包提供只开放“只说与动”的 MCP 桥接程序，并在设置的“MCP 接入”页提供同一个开关与可直接复制的客户端配置。标准版仍在“Agent 与 MCP”页底部提供这一开关。外部客户端可用的工具严格限制为 `list_actions`、`play_action`、`interrupt_action`、`say` 和 `say_next`；设置修改、两项截图、Agent 对话、输入框和预约都不可用，交互记录资源为空且不能订阅。开关默认关闭，开启前会弹窗说明风险；关闭开关、关闭连接或退出桌宠后该服务与管道立即释放。

Windows 标准版下载 `BigBlueFish-v1.1.30-OCR-Setup.exe`，无 ACP 版下载 `BigBlueFish-v1.1.30-Safe-OCR-Setup.exe`；Ubuntu／Debian x64 标准版下载 `BigBlueFish-v1.1.30-Linux-x64.deb`，无 ACP 版下载 `BigBlueFish-v1.1.30-Safe-Linux-x64.deb`。文件名中的 `Safe` 对应无 ACP 版，各安装包均提供配套 `.sha256` 校验文件。已安装的用户可以在设置中检查更新直接升级，也可以手动运行新版安装器；Windows 上选择原安装目录即可保留模型与设置。

连接方式与适配列表见 [连接本机 CLI Harness](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.30/docs/ACP.md)，内部工具参数见 [内部工具参考](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.30/docs/MCP.md)，完整行为见 [功能规格](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.30/docs/AGENT_FUNCTIONAL_SPEC.md)，版本变化见 [更新日志](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.30/CHANGELOG.md)。
