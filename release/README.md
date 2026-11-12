# 蓝色大肥鱼 · 1.1.25

修复桌宠窗体鼠标输入被传递给后方程序的问题：透明区域也能接收鼠标操作，左键可拖动整只桌宠，右键可打开菜单，同时保持置顶。设置页中的本地 OpenAI 兼容 API 地址支持 `/api` 等任意路径前缀，也可直接填写完整的 `chat/completions` 地址。

Windows 标准版下载 `BigBlueFish-v1.1.25-OCR-Setup.exe`，无 ACP 版下载 `BigBlueFish-v1.1.25-Safe-OCR-Setup.exe`；Ubuntu／Debian x64 标准版下载 `BigBlueFish-v1.1.25-Linux-x64.deb`，无 ACP 版下载 `BigBlueFish-v1.1.25-Safe-Linux-x64.deb`。文件名中的 `Safe` 对应无 ACP 版，各安装包均提供配套 `.sha256` 校验文件。已安装的用户可以在设置中检查更新直接升级，也可以手动运行新版安装器；Windows 上选择原安装目录即可保留模型与设置。

连接方式与适配列表见 [连接本机 CLI Harness](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.25/docs/ACP.md)，内部工具参数见 [内部工具参考](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.25/docs/MCP.md)，完整行为见 [功能规格](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.25/docs/AGENT_FUNCTIONAL_SPEC.md)，版本变化见 [更新日志](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.25/CHANGELOG.md)。
