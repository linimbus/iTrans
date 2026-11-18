# itrans · Windows 截图翻译

Windows 10 / 11 原生截图 OCR 翻译工具：按下全局快捷键框选屏幕任意区域，调用 Windows 自带 OCR 识别文字，再通过本地 LibreTranslate 服务完成中文 ⇄ 英文互译。翻译窗口分为上下两栏，上方是识别结果，下方是翻译结果。

## 功能

- 全局快捷键（默认 `Ctrl+Alt+Q`）触发框选截图，支持多显示器与任意区域
- Windows 系统自带 OCR（`Windows.Media.Ocr`），无需额外识别服务
- 自动检测源语言，中文 → 英文、英文 → 中文
- 识别结果可编辑，可一键重新翻译
- 一键复制原文 / 译文
- 识别与翻译内容支持自定义字体、字号
- OCR 语种可勾选：简体中文、繁体中文、English
- 翻译方向可选：自动（中文 ⇄ 英文）、中文 → 英文、英文 → 中文
- 支持随系统启动（写入当前用户注册表启动项）
- 简洁主界面，内置 256px 高清图标（`src/Itrans/Assets/itrans.ico`）
- 关闭或最小化窗口后程序继续在系统托盘（右下角）运行
- 托盘菜单：截图翻译、显示主窗口、设置、退出
- 服务地址、API Key、快捷键、OCR 语种、翻译方向、字体、开机启动均可在设置中修改

## 环境要求

- Windows 10 1809+ 或 Windows 11
- .NET 10 Desktop Runtime（从源码运行或框架依赖发布时）
- Windows OCR 语言包：按需安装中文（简体/繁体）与英文
  - 设置 → 时间和语言 → 语言和区域 → 添加语言
  - 在语言选项中安装“光学字符识别”(OCR)
- Docker Desktop（运行 LibreTranslate）

## 启动翻译服务

项目根目录提供了 `docker-compose.yml`：

```bat
docker compose up -d
```

- 默认监听 `http://localhost:5000`
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

## 设置

点击主窗口“设置”或托盘菜单“设置…”可修改：

- LibreTranslate 服务地址（如 `http://localhost:5000`）
- API Key（公共实例可能需要）
- 截图快捷键（格式如 `Ctrl+Alt+Q`，支持 A-Z、0-9、F1-F12 等）
- OCR 识别语种：简体中文、繁体中文、English，可多选（至少一种）
- 翻译方向：自动检测（中文 ⇄ 英文）、中文 → 英文、英文 → 中文
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
