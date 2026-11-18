# 蓝色大肥鱼 · 1.1.29

设置页新增外部 MCP 接入。标准版可以在“Agent 与 MCP”（原“连接 Agent”）页底部单独打开“允许外部 MCP 客户端控制鱼鱼（只说与动）”，把页面给出的配置加入 Claude Desktop、Cursor、Codex 等支持 MCP 的客户端并重启，就能让鱼鱼说出指定内容、播放指定动作，不需要连接本机 Agent，也不依赖本地模型。外部客户端只得到 `list_actions`、`play_action`、`interrupt_action`、`say` 和 `say_next` 五个工具；设置修改、两项截图、Agent 对话、输入框和预约都不可用，交互记录资源为空且不能订阅。开关默认关闭，开启前会弹窗说明风险；关闭开关、关闭连接或退出桌宠后该服务与管道立即释放。

Windows 标准版下载 `BigBlueFish-v1.1.29-OCR-Setup.exe`，无 ACP 版下载 `BigBlueFish-v1.1.29-Safe-OCR-Setup.exe`；Ubuntu／Debian x64 标准版下载 `BigBlueFish-v1.1.29-Linux-x64.deb`，无 ACP 版下载 `BigBlueFish-v1.1.29-Safe-Linux-x64.deb`。文件名中的 `Safe` 对应无 ACP 版，各安装包均提供配套 `.sha256` 校验文件。已安装的用户可以在设置中检查更新直接升级，也可以手动运行新版安装器；Windows 上选择原安装目录即可保留模型与设置。

连接方式与适配列表见 [连接本机 CLI Harness](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.29/docs/ACP.md)，内部工具参数见 [内部工具参考](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.29/docs/MCP.md)，完整行为见 [功能规格](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.29/docs/AGENT_FUNCTIONAL_SPEC.md)，版本变化见 [更新日志](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.29/CHANGELOG.md)。
