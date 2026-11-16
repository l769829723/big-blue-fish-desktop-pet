# 蓝色大肥鱼 · 1.1.27

修复本地 OpenAI 兼容 API 使用思考模型（qwen3、deepseek-r1 等）时吐字测试只提示“未返回可用吐字内容”的问题：这些模型会把 180 token 的说话预算全部用在思考内容上，正文为空。现在检测到只有思考内容时，自动带 `reasoning_effort=none` 重试一次（Ollama 会据此关闭该模型的思考）；重试仍无正文时提示换用非思考模型。服务没有返回流式内容、或返回的不是 `chat/completions` 格式时，错误提示会分别指出应检查 OpenAI 兼容地址还是响应格式。

Windows 标准版下载 `BigBlueFish-v1.1.27-OCR-Setup.exe`，无 ACP 版下载 `BigBlueFish-v1.1.27-Safe-OCR-Setup.exe`；Ubuntu／Debian x64 标准版下载 `BigBlueFish-v1.1.27-Linux-x64.deb`，无 ACP 版下载 `BigBlueFish-v1.1.27-Safe-Linux-x64.deb`。文件名中的 `Safe` 对应无 ACP 版，各安装包均提供配套 `.sha256` 校验文件。已安装的用户可以在设置中检查更新直接升级，也可以手动运行新版安装器；Windows 上选择原安装目录即可保留模型与设置。

连接方式与适配列表见 [连接本机 CLI Harness](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.27/docs/ACP.md)，内部工具参数见 [内部工具参考](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.27/docs/MCP.md)，完整行为见 [功能规格](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.27/docs/AGENT_FUNCTIONAL_SPEC.md)，版本变化见 [更新日志](https://github.com/l769829723/big-blue-fish-desktop-pet/blob/v1.1.27/CHANGELOG.md)。
