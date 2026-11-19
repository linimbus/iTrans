# itrans · Windows 截图翻译

Windows 10 / 11 原生截图 OCR 翻译工具：按下全局快捷键框选屏幕任意区域，调用 Windows 自带 OCR 识别文字，再通过 LibreTranslate 服务完成中文 ⇄ 英文互译。翻译窗口分为上下两栏，上方是识别结果，下方是翻译结果。除截图外，还支持上传文档、翻译后自动下载译文。默认服务地址为 `http://192.168.3.5:5000`。

## 功能

- 全局快捷键（默认 `Ctrl+Alt+Q`）触发框选截图，支持多显示器与任意区域
- Windows 系统自带 OCR（`Windows.Media.Ocr`），无需额外识别服务
- 自动检测源语言，中文 → 英文、英文 → 中文
- 识别结果可编辑，可一键重新翻译
- 一键复制原文 / 译文
- 文件翻译：批量上传文档，调用 LibreTranslate `/translate_file` 翻译后自动下载译文
- 文件格式由服务端 `/frontend/settings` 的 `supportedFilesFormat` 返回（当前：`.txt` `.odt` `.odp` `.docx` `.pptx` `.epub` `.html` `.srt` `.pdf`）
- 识别与翻译内容支持自定义字体、字号
- OCR 语种可勾选：简体中文、繁体中文、English
- 翻译方向可选：自动（中文 ⇄ 英文）、中文 → 英文、英文 → 中文
- 支持随系统启动（写入当前用户注册表启动项）
- 简洁主界面，内置 256px 高清图标（`src/Itrans/Assets/itrans.ico`）
- 关闭或最小化窗口后程序继续在系统托盘（右下角）运行
- 托盘菜单：截图翻译、文件翻译、显示主窗口、设置、退出
- 服务地址、API Key、快捷键、OCR 语种、翻译方向、文件翻译超时、字体、开机启动均可在设置中修改
- 主窗口标题显示程序名称与版本号（版本来自项目文件 `<Version>`，当前为 v1.0.0）

## 环境要求

- Windows 10 1809+ 或 Windows 11
- .NET 10 Desktop Runtime（从源码运行或框架依赖发布时）
- Windows OCR 语言包：按需安装中文（简体/繁体）与英文
  - 设置 → 时间和语言 → 语言和区域 → 添加语言
  - 在语言选项中安装“光学字符识别”(OCR)
- Docker Desktop（运行 LibreTranslate）

## 翻译服务

默认连接局域网服务 `http://192.168.3.5:5000`，可在设置中修改。该服务需提供以下 LibreTranslate 兼容接口：

- `GET /languages` 与 `GET /frontend/settings`
- `POST /translate`
- `POST /translate_file`（`multipart/form-data`：`file`、`source`、`target`，可选 `api_key`）
- 文件翻译响应为 JSON `{ "translatedFileUrl": "..." }`，程序会继续 `GET` 该地址下载译文

如需在本机运行服务，项目根目录提供了 `docker-compose.yml`：

```bat
docker compose up -d
```

- 默认监听 `http://localhost:5000`，请在设置中把服务地址改为 `http://localhost:5000`
- 首次启动会下载 en / zh 模型，可能需要数分钟，可通过 `docker compose logs -f` 观察进度
- 已通过 `LT_LOAD_ONLY=en,zh` 限制加载的语言

## 构建与运行

```bat
dotnet run --project src/Itrans
```

打包为独立单文件（目标机器无需安装 .NET）：

```bat
dotnet publish src/Itrans -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

生成的可执行文件位于 `publish/Itrans.exe`，可直接拷贝到其他 Windows 10/11 机器运行（仍需系统 OCR 语言包与 LibreTranslate 服务）。

也可以直接运行项目根目录的 `build.bat`（双击即可），脚本会自动检查 SDK、执行发布并在成功或失败后暂停显示结果；若提示 `publish\Itrans.exe` 被占用，请先退出正在运行的 Itrans。传入 `--nopause` 参数可在命令行中跳过结束暂停。

## 使用

1. 启动 `Itrans.exe`，主窗口与托盘图标出现。
2. 按 `Ctrl+Alt+Q`（或点击“截图翻译”），屏幕被暗化，拖动鼠标框选区域。
   - `Esc` 或鼠标右键取消。
3. 松开鼠标后自动进行 OCR 与翻译，结果显示在窗口中。
4. 关闭窗口只会隐藏到托盘，程序不会退出；从托盘菜单选择“退出”才会结束。

## 文件翻译

1. 点击主窗口或托盘菜单中的“文件翻译…”。
2. 选择翻译方向（自动检测 → 中文/英文、中文 → 英文、英文 → 中文）。
3. 点击“添加文件…”，可一次选择多个文档；输出目录留空时译文保存到源文件所在目录。
4. 点击“开始翻译”，程序逐个上传文件，完成后自动下载译文；双击列表项可在资源管理器中定位文件。
5. 支持格式以服务端返回为准，当前为 `.txt` `.odt` `.odp` `.docx` `.pptx` `.epub` `.html` `.srt` `.pdf`。
6. 单文件默认最大等待 600 秒，可在设置中调整（30–3600 秒）。服务端默认网页的文件翻译请求未设置超时，`frontendTimeout`（500）只是文本输入的防抖延迟，与文件翻译无关。

翻译请求遵循 LibreTranslate 定义：`POST /translate_file` 使用 `multipart/form-data`，参数为 `file`、`source`（语言代码或 `auto`）、`target` 与可选的 `api_key`；返回 `translatedFileUrl` 后由程序下载保存。

## 设置

点击主窗口“设置”或托盘菜单“设置…”可修改：

- LibreTranslate 服务地址（默认 `http://192.168.3.5:5000`）
- API Key（公共实例可能需要）
- 截图快捷键（格式如 `Ctrl+Alt+Q`，支持 A-Z、0-9、F1-F12 等）
- OCR 识别语种：简体中文、繁体中文、English，可多选（至少一种）
- 翻译方向：自动检测（中文 ⇄ 英文）、中文 → 英文、英文 → 中文
- 文件翻译超时：单文件最长等待时间，默认 600 秒，可设置 30–3600 秒
- 内容字体与字号（字体为系统已安装字体，字号 8–72）
- 随系统启动：登录 Windows 后自动运行

设置保存在 `%APPDATA%\Itrans\settings.json`。

## 常见问题

- **快捷键无效**：可能被其他程序占用，请更换快捷键；注册失败时托盘会弹出提示。
- **未识别到文字 / 缺少语言包**：在 Windows 语言设置中为中文（简体）安装“光学字符识别”可选功能。
- **翻译失败**：
  - 确认 LibreTranslate 已启动：浏览器访问 `http://localhost:5000/languages`
  - 在设置中点击“测试连接”检查中文、英文模型是否加载
  - 首次翻译中文时，LibreTranslate 可能需要联网下载 Argos 中文模型
- **翻译服务需要鉴权**：在设置中填写 API Key。
- **文件翻译失败**：确认服务端已启用文件翻译（`/frontend/settings` 中 `filesTranslation` 为 `true` 且 `supportedFilesFormat` 非空），并检查文件大小是否超过服务端限制；不支持的扩展名会添加时被跳过。
- **文件翻译超时**：大文件可能需要更长时间，默认超时为 600 秒，可在设置中增大到最多 3600 秒后重试。
