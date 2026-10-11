# AI Image Editing Tools — Offline AI QR and cross-platform updates / 本地 AI 二维码与跨平台更新

2026-10-12 · Windows EXE / Android APK

## English

This update adds local AI QR fusion and improves import, placement, prompts, model management and export on Windows and Android. Existing dual-image PNGs, background reveal, scrambling, file packaging, GIFs, metadata cleanup, redaction, parameter reading and ordinary QR tools remain available without an account.

### Local AI QR fusion

- DreamShaper 8 (SD 1.5) with QR Monster SD 1.5 ControlNet is the default. One-click AI generation prepares missing resources automatically. SDXL Base 1.0 / QR Monster SDXL is labeled an experimental alternative. Compatible local models require matching model families; multiple setups can coexist.
- Extract QR payloads from QQ, WeChat and other QR images or screenshots, then embed the QR into a separate source image. Avatars, names and screenshot UI are excluded; choose a code when several are detected.
- Manual placement starts with the source image and an ordinary QR positioning preview. Adjust its size and position before generation; the preview then shows the actual fused result. Keep the full source canvas and add text after generation.
- Automatic captioning is independent of placement and remains available during manual positioning; turn it off to edit prompts. Automatic skin/hair avoidance and manual placement cannot both be checked, but both can be off to use the manual position. If manual generation fails scan checks, the result dialog offers an automatic-avoidance retry.
- Automatic prompts describe source style and local garment, building or foliage materials while retaining source colors. Prompt templates and generation parameters are saved per model family and model file.
- Improve locator/background integration. Supported Windows CUDA paths attempt point-based locators and texture refinement. Android uses actual native diffusion guided by circular centers and surrounding points, retaining source texture without square white or blurred backing plates. AI results are accepted only after full-size and reduced-size scan validation.

### Automatic setup and model management

- Download and verify resources in the background on both platforms. Show verified file count / total, aggregate byte progress, current file sizes and live transfer speed. Other tools remain usable; Ready requires every mandatory resource to pass verification.
- Choose automatic, official or mainland-China mirror sources. Automatic selection combines network probes and regional settings with fallback sources; throughput depends on connectivity and server load.
- Find, verify and reuse existing models, repair missing or damaged resources and resume interrupted downloads. Display actual storage locations, with folder opening on Windows and an in-app file listing on Android.
- New automatic Windows storage checks D, E, F and subsequent data drives for space and creates `AI-Models` for models and private inference environments. It does not fall back to C automatically; manual folder selection remains available. Android uses `AI-Models` in app storage and includes inference libraries in the APK.
- Delete selected models or clean all managed AI resources after confirmation. Referenced external models and source images are preserved. Android's bundled runtime is removed by uninstalling the app.

### Shared workflows and fixes

- Fix empty-preview image selection. Windows accepts dropped images; Android uses its system picker. Add Clear preview and View top preview, with explicit guidance that the fused result appears at the top of the page.
- Synchronize the independent prompt switch, placement-control visibility, model setup selection, saved parameters and bilingual tutorials.
- Improve batch processing of arbitrary checked queue items, RGBA / HEX colors, GIF playback and looping, generation-parameter reading and Copy all, plus persistent numbering, size and GIF defaults.
- Retain the full canvas and source aspect ratio, avoid overwriting existing output names and allow text after generation. Both READMEs and the guides bundled in EXE / APK are synchronized.

### Installation and requirements

- **Windows:** download the EXE; .NET Framework 4.8 is required. Ordinary tools need no AI models. First AI use needs connectivity for the private environment and models, without a separate Python, ComfyUI or WebUI installation. Prepared inference works offline on supported NVIDIA CUDA or CPU; CPU inference is slower.
- **Android:** ordinary tools target Android 5 and later. Offline AI requires a 64-bit system on Android 7 or later. Native CPU diffusion and ONNX Runtime are bundled; once models are prepared, captioning and generation work offline without a computer. Generation requires at least 3 GiB of available system memory for SD 1.5 or 9 GiB for SDXL, with explicit insufficient-memory errors.
- Networking is required for model downloads. Images and prompts are not uploaded. Models and inference dependencies require additional disk space beyond the EXE / APK sizes.

### Validation and limitations

Windows passed core and bilingual UI regression across 11 pages. Android passed core, UI, model-management and formal-package installation checks on an API 35 x86_64 emulator. Actual QQ and WeChat screenshots passed payload extraction and re-encoding checks.

A complete offline Android run used real captioning/segmentation models, DreamShaper 8 and QR Monster to fuse a manually placed QR into one complex image. It retained the full 1920×1280 canvas. After local tonal correction, two independent decoders confirmed the payload at full size and 900×600; generation took about 12 minutes 50 seconds.

Locator points can remain visible and naturalness does not yet match every reference. Windows CUDA and Android CPU differ in output and speed. SDXL is experimental; physical ARM64 performance and compressed QQ / WeChat transfers are not comprehensively verified. Compatibility is not guaranteed for every model, selected region or phone released since 2024. See [Validation](https://github.com/pkh2388235687-star/AI-Image-Toolbox/blob/main/docs/VALIDATION.en.md) and [Platform comparison](https://github.com/pkh2388235687-star/AI-Image-Toolbox/blob/main/docs/PLATFORM_PARITY.en.md).

## 简体中文

本次更新加入本地 AI 二维码融合，并同步完善 Windows 与 Android 的导入、定位、提示词、模型管理和导出操作。保留双图切换、背景显图、图片混淆、文件伪装、GIF、清信息、打码、参数读取及普通二维码工具，无需账号。

### 本地 AI 二维码融合

- 默认方案为 DreamShaper 8（SD 1.5）与 QR Monster SD 1.5 ControlNet，点击“一键 AI 生成”自动准备缺失资源。SDXL Base 1.0 / QR Monster SDXL 标注为实验备用；支持同家族的兼容本地模型，多种方案可同时保留。
- 可从 QQ、微信及其他二维码图片或截图提取二维码内容，再单独融合进底图。截图中的头像、昵称与界面不会一并贴入底图；识别到多个码时可选择。
- 手动放置时，预览先显示底图与普通二维码定位示意，可调大小和位置；生成后改为显示真实融合结果。保留完整原图画布，生成后可继续添加文字。
- “自动识别画面并生成提示词”独立于位置选项，手动放置也能自动生成提示词，关闭后可自行编辑。“自动避开皮肤与头发放置”与“手动放置”最多选择一个，也可以都关闭并按手动位置生成。手动生成扫码失败后，结果框提供“帮我选择自动避让”重试。
- 自动提示词按画面风格和选区材质描述织物、建筑或植物，配色沿用原图。提示词模板与生成参数按模型家族、模型文件保存，换模型后可单独调整。
- 优化定位信息与背景的融合。Windows 支持在适用的 CUDA 路径尝试点状定位与纹理修整；Android 使用真实原生扩散，以圆形中心和周围小点引导，保留原图纹理，不添加方形白色或模糊底板。只有通过完整图及缩图扫码校验的结果才作为 AI 成品。

### 自动准备与模型管理

- 两端后台下载与校验，显示已校验文件数 / 总数、总体字节进度、当前文件大小和实时速度。准备过程中可使用其他工具；全部必需文件通过校验后才显示就绪。
- 下载源可选自动、官方或中国大陆镜像。自动模式结合网络探测及系统地区选择可达来源，失败时尝试备用；实际速度取决于网络和源负载。
- 检测、校验并复用已有模型，缺失或损坏资源按需补齐，支持断点续传。显示真实存放位置；Windows 可打开所在文件夹，Android 可查看应用内的实际文件清单。
- Windows 新的自动存储位置按 D、E、F 等数据盘顺序检查空间，创建 `AI-Models`，模型与私有生图环境放在一起；不自动回落到 C 盘，仍可手动选目录。Android 使用应用存储中的 `AI-Models`，生图运行库随 APK 安装。
- 可删除选中模型或清理全部受管 AI 资源，操作前确认。引用的外部模型与原图不删除；Android 内置运行库通过卸载应用移除。

### 两端操作与修复

- 修复空预览点击选图，Windows 支持拖入图片，Android 通过系统选择器导入；加入“清空预览”和“查看顶部预览”，明确融合成品显示在页面最上方。
- 同步独立提示词开关、定位控件的显示逻辑、模型方案选择、参数保存及双语教程，避免位置选项隐藏提示词入口。
- 完善任意勾选队列的批量处理、RGBA / HEX 颜色、GIF 播放与循环、生成参数读取与全部复制，以及编号、尺寸和 GIF 默认值保存。
- 保留完整画布与源比例，输出文件避免重名覆盖；图片生成后可添加文字。中英文 README 与 EXE / APK 内的教程内容同步。

### 安装与运行

- **Windows：**下载 EXE，系统需有 .NET Framework 4.8。普通工具不需要 AI 模型；首次 AI 生成需要联网准备私有环境和模型，无需另装 Python、ComfyUI 或 WebUI。准备后可离线推理，支持适用的 NVIDIA CUDA 或 CPU，CPU 较慢。
- **Android：**普通工具最低 Android 5；手机离线 AI 需要 Android 7 或更高版本的 64 位系统。APK 内置原生 CPU 扩散与 ONNX Runtime，模型下载完成后无需电脑即可离线识图、生图。SD 1.5 生成前至少需要 3 GiB 可用系统内存，SDXL 至少 9 GiB；不足时会明确提示。
- 模型下载需要联网权限，图片和提示词不上传。模型及推理依赖需要额外磁盘空间，不包含在 EXE / APK 的安装包体积中。

### 验证与已知限制

Windows 完成 11 个页面的双语界面与核心功能回归；Android 完成 API 35 x86_64 模拟器中的核心、界面、模型管理及正式 APK 安装检查。真实 QQ、微信截图均完成提取与重新编码核对。

Android 在断网状态下使用真实识图模型、DreamShaper 8 和 QR Monster 完成一张复杂图的手动融合，保留完整 1920×1280 画布；经局部亮度校准后，两种独立解码器在完整图与 900×600 缩图上核对内容通过，耗时约 12 分 50 秒。

定位点仍可能可见，融合自然度尚未达到所有参考案例。Windows CUDA 与 Android CPU 的效果和速度存在差异；SDXL 为实验备用，ARM64 真机性能及 QQ / 微信压缩传输尚未全面验证。不能保证任意模型、任意选区或所有 2024 年以后上市的手机均可用。详见 [验证范围](https://github.com/pkh2388235687-star/AI-Image-Toolbox/blob/main/docs/VALIDATION.md) 与 [两端功能对照](https://github.com/pkh2388235687-star/AI-Image-Toolbox/blob/main/docs/PLATFORM_PARITY.md)。

## Downloads and integrity / 下载与完整性

Download the two packages from this release's Assets. / 请从本次发布的 Assets 下载两个安装包。

| Package / 安装包 | Size / 大小 | SHA-256 |
| --- | ---: | --- |
| `AI-Image-Editing-Tools.exe` | 2,271,232 bytes (2.17 MiB) | `8f11a287c07d57b668543d106dc1716c34a21d6d542868b10c2f66e41a5b818b` |
| `AI-Image-Editing-Tools.apk` | 48,436,566 bytes (46.19 MiB) | `f85a352b4d70186ac1da07cf483dccac3c42dcb0021958729bbf1e29fa3cd865` |

APK signer certificate SHA-256 / APK 签名证书 SHA-256：`4a6e6b133417dd34f3c4b357590e7500f7d92daada25b2f39621ab7b598f1508`

Author / 作者：Guangying Ruoshui / 光影若水 · [MIT License](https://github.com/pkh2388235687-star/AI-Image-Toolbox/blob/main/LICENSE). Third-party code and models retain their own licenses; see [Third-party notices](https://github.com/pkh2388235687-star/AI-Image-Toolbox/blob/main/THIRD_PARTY_NOTICES.md) and [Android native notices](https://github.com/pkh2388235687-star/AI-Image-Toolbox/blob/main/android/native/THIRD_PARTY_NOTICES.md).
