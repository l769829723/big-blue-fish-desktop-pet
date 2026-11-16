# 蓝色大肥鱼 · 1.1.26

修复桌宠显示时左键拖动和右键菜单失效的问题：软件渲染后动画不再经过原生子窗口，点击因此被判为穿透；现在角色可见身体范围带一层几乎不可见的命中表面，窗口过程直接处理左右键与拖动中的鼠标移动，并使用系统鼠标捕获，拖动和菜单不再依赖窗体激活判定。身体以外的透明区域仍可点击下层应用，任务栏按钮继续保持隐藏。本地 API 后端新增可填写的模型名称，修复 Ollama 等需要真实模型名的 OpenAI 兼容服务无法使用的问题；只填写 `http://127.0.0.1:11434` 这类主机和端口时自动补全 `/v1/chat/completions`，API 返回错误时显示 HTTP 状态与简短原因。

Windows 标准版下载 `BigBlueFish-v1.1.26-OCR-Setup.exe`，无 ACP 版下载 `BigBlueFish-v1.1.26-Safe-OCR-Setup.exe`；Ubuntu／Debian x64 标准版下载 `BigBlueFish-v1.1.26-Linux-x64.deb`，无 ACP 版下载 `BigBlueFish-v1.1.26-Safe-Linux-x64.deb`。文件名中的 `Safe` 对应无 ACP 版，各安装包均提供配套 `.sha256` 校验文件。已安装的用户可以在设置中检查更新直接升级，也可以手动运行新版安装器；Windows 上选择原安装目录即可保留模型与设置。

连接方式与适配列表见 [连接本机 CLI Harness](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.26/docs/ACP.md)，内部工具参数见 [内部工具参考](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.26/docs/MCP.md)，完整行为见 [功能规格](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.26/docs/AGENT_FUNCTIONAL_SPEC.md)，版本变化见 [更新日志](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.26/CHANGELOG.md)。
