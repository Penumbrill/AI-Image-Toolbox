# AI Image Editing Tools v0.3.0 — Image Scrambling, Video Metadata Cleanup & Web App / 图片混淆、视频清信息与网页版

2026-10-14 · Windows EXE / Android APK / Web

## 网页移动端更新 / Mobile web update · 2026-10-14

网页版手机操作：统一点击“导入图片”进入系统图片选择器，支持多选；拍摄后先保存到相册再导入。移除独立拍照和文件按钮。图片解码支持浏览器回退，默认像素预算可容纳常见 1200 万像素照片，低内存设备仍有限制；不会自动缩放。HEIC/HEIF 取决于浏览器支持，无法打开时请另存 JPG/PNG。手机可长按处理结果保存，下载文件在浏览器下载列表或系统“文件”中查看。内置浏览器受限时使用 Safari/Chrome。 EXE/APK 与签名保持不变。

Mobile web: use the single Import images control for the system image picker, with multiple selection. Save a new photo to the library before importing; separate camera and file controls are removed. Image decoding has a browser fallback. The default pixel budget accommodates common 12 MP photos, with a lower limit on low-memory devices; images are never resized automatically. HEIC/HEIF depends on browser support: export JPEG/PNG if decoding fails. Touch and hold the processed image to save, or find downloads in browser downloads/Files. Use Safari/Chrome if an in-app browser restricts these actions. EXE/APK and signing are unchanged.

## 账号与链接同步 / Account and link synchronization · 2026-10-14

GitHub 用户名更新为 `Penumbrill`，仓库和网页入口同步为 `Penumbrill/AI-Image-Toolbox` 与 `penumbrill.github.io/AI-Image-Toolbox/`。本地源码、HTML、中英文文档及两端内置项目链接同步；重新构建 EXE/APK，保留版本 0.3.0、Android 应用身份和原签名。旧 Pages 网址不自动跳转，请更新书签。

The GitHub username is now `Penumbrill`. Repository and website links use `Penumbrill/AI-Image-Toolbox` and `penumbrill.github.io/AI-Image-Toolbox/`. Synchronize local source, HTML, bilingual documentation and both clients' embedded project links. Rebuild EXE/APK while retaining version 0.3.0, Android application identity and the existing signer. The previous Pages URL does not redirect; update bookmarks.

对照线上旧版 main `d57e9197639bf2e701c091c4f8104805af093b3f`（v0.2.0）整理。保留原有 AI 二维码等工具；以下仅列本轮新增与变更。
Compared with online main `d57e9197639bf2e701c091c4f8104805af093b3f` (v0.2.0). Existing tools, including local AI QR generation, are retained; the changes below describe this update.

## 中文

- 图片混淆统一为 Gilbert 曲线流程，默认系数 1.0、1 次、无损 PNG、原尺寸、颜色调整关闭。一键生成；分别设置混淆/解混淆次数（1–20）与系数（0–2），支持重复混淆与逆向还原。当前页面移除原文件附件恢复等冗余入口，导出普通图片。
- Windows、Android 与网页共同搜索解混淆系数（步进 0.1）和次数，展示三个还原预览。相同置换归为一组；候选需确认，无法承诺唯一识别未知参数。
- 新增单文件独立网页版：本地 Worker 处理、批量队列、选中项/全部处理、PNG/JPG 导出、ZIP 下载、取消、预览与中英切换。操作区位于更多设置上方，按钮按功能三列等宽，两栏标题、输入、快捷档及勾选项对齐；适配窄屏。
- EXE/APK 的生成与批量按钮统一放入保存位置区域。清信息增加图片、视频两个独立板块；视频支持 MP4/MOV/M4V、WebM/MKV 的容器工作流、提示词、文本、日期及附件清理，不重编码音视频，保留旋转、音轨、时长、帧率与尺寸。
- 双图切换和文件伪装提供默认关闭的封面像素混淆试用。隐藏图与附件保持原有封装，可直接使用原功能还原；文字和编号在混淆后绘制。
- 同步中英文 README、内置教程、项目结构、功能对照与验证说明；普通界面文案简化，必要许可声明保留。发布目录仅保留 EXE/APK，大小和 SHA-256 列在下方。

## English

- Unify image scrambling around the Gilbert workflow. Defaults: shift 1.0, one round, lossless PNG, original dimensions and color adjustments off. Generate with one click; independently choose forward/reverse rounds (1–20) and shifts (0–2). Remove redundant original-file attachment recovery controls from the current page; export ordinary images.
- Search restore shifts (step 0.1) and rounds in Windows, Android and web, grouping identical permutations and showing three actual previews for confirmation. Unknown parameters cannot always be identified uniquely.
- Add a standalone, self-contained web app with local Worker processing, batch queues, checked/all operations, PNG/JPEG export, ZIP downloads, cancellation, previews and Chinese/English switching. Actions precede advanced settings; functional button rows have three equal columns, paired settings align, and narrow-screen layouts are supported.
- Move EXE/APK generation and batch actions into the destination area. Split metadata removal into independent image/video panels. Video cleanup supports MP4/MOV/M4V and WebM/MKV container workflows, prompts, text, dates and attachments without re-encoding audio/video, preserving rotation, audio, duration, frame rate and dimensions.
- Add an optional cover-only scramble trial for dual-image and file packaging, off by default. Hidden images and attachments keep their packaging and use the existing restore tools; text and numbering are drawn afterwards.
- Synchronize bilingual READMEs, bundled tutorials, project structure, parity and validation notes. Simplify ordinary copy while retaining required license notices. The release folder contains only EXE/APK; sizes and SHA-256 follow below.

## 验证与限制 / Validation and limits

六组联合参数候选的 C#、Android Java、网页排序和评分一致；七组带工作流的视频清理前后音视频编码包和解码内容哈希不变，C#/Java 输出逐字节一致。Windows 完整界面回归包括选中视频导出、图片/视频队列独立、取消和语言切换。APK 沿用原签名证书。

Six joint-search samples have identical candidates and scores in C#, Android Java and web. Seven tagged-video cases retain encoded-packet and decoded audio/video hashes, with byte-identical C#/Java outputs. Windows UI regression covers selected video exports, independent queues, cancellation and language switching. APK signing is unchanged.

候选不是确定识别；缩放、重编码或颜色调整可能导致还原失败。Bilibili 请先查看并保存原图。已有普通混淆图发布通过反馈只代表该样本，不承诺平台 100% 通过。封面试用尚未平台验证；Android/iOS 真机验证待用户实测。视频清理仅针对支持的容器元数据，不清除画面水印或编码流内信息；不支持的结构报错。更多细节见 [VALIDATION.md](VALIDATION.md)。

Candidates are not definitive identification. Resizing, re-encoding or color adjustments can prevent restoration. Save the original image on Bilibili. The reported successful ordinary-scramble upload applies only to that sample, not a guaranteed acceptance rate. Cover trials and physical Android/iOS validation remain pending. Video cleaning targets supported container metadata, not visible watermarks or data inside encoded streams; unsupported structures report an error.

Web: [图片混淆 / Image scramble](https://penumbrill.github.io/AI-Image-Toolbox/)

| Package | Size | SHA-256 |
| --- | ---: | --- |
| Windows/AI-Image-Editing-Tools.exe | 2328576 bytes | `228b9306ef0b766fe595d536cc6205cc46b3b06fc1802db5657bc48f66400dc4` |
| Android/AI-Image-Editing-Tools.apk | 48473430 bytes | `27fea346940dab69f08a07b86b2a224971c9d30015d6b51e534860f756ec9d9c` |

APK signer SHA-256: `4a6e6b133417dd34f3c4b357590e7500f7d92daada25b2f39621ab7b598f1508` (unchanged).

# 1.2.3 双图与文件封面混淆试用 / Cover-only scrambling trial

2026-10-13 · 本地待实测，未上传 / Local review, not uploaded

Windows 与 Android 的双图切换和文件伪装新增“封面像素混淆（实验）”，默认关闭。只在绘制文字与编号前混淆封面，双图隐藏内容与文件封装保持原格式，可直接使用原功能还原，无需先解混淆。

Windows and Android add an optional cover-only scramble to dual-image and file packaging. It is off by default and runs before text/numbering. Hidden content and packaged files keep their formats and use the original restore tools without a separate descramble step.

本地两组双图与八组文件封装完成逐像素或逐字节恢复；Android 封面处理类在主机 JVM 的六组像素对照、尺寸及取消检查通过。重新编码会丢失动态帧或附件，这项试用不能承诺平台审核通过率。用户反馈普通混淆图 Bilibili 上传未屏蔽、发布通过；双图与文件的新模式尚未在平台实测。完整验证见 [VALIDATION.md](VALIDATION.md)。

| Package | Size | SHA-256 |
| --- | ---: | --- |
| Windows/AI-Image-Editing-Tools.exe | 2312192 bytes | `ed1b23582374e6d6fedcc151778e360703ca80ea2a565a6fa7c8d401f245064f` |
| Android/AI-Image-Editing-Tools.apk | 48465238 bytes | `ec4ce1a08be488e91f8d813658af9cdfc35889d4269e53c571903573bddc3bcb` |

APK signer SHA-256: `4a6e6b133417dd34f3c4b357590e7500f7d92daada25b2f39621ab7b598f1508` (unchanged).

# 1.2.2 保存区操作与文案整理 / Destination actions and clearer copy

2026-10-13 · 本地待检查，未上传 / Local review, not uploaded

Windows 与 Android 将生成混淆图及四个批量按钮集中到保存位置区域，沿用其他工具的操作区样式。网页版先显示处理与下载，再显示默认收起的更多设置。移除界面、README 与普通文案中的作者展示和外部算法站点描述；许可证正文保留所需版权声明。中英文 README 新增项目结构，两端教程同步更新。

Windows and Android group Generate and the four batch buttons in the destination area. Web actions precede collapsed advanced settings. Creator displays and external algorithm-site descriptions are removed from ordinary copy; required copyright notices remain in license texts. Both READMEs add the project structure and bundled guides are synchronized.

算法函数保持不变。Windows 完整界面回归通过，包括保存区按钮实际点击、两次批量还原、中英切换和布局检查。网页操作顺序与桌面/窄屏三列等宽通过检查。Android 编译及原签名校验通过，真机未实测。

| Package | Size | SHA-256 |
| --- | ---: | --- |
| Windows/AI-Image-Editing-Tools.exe | 2308608 bytes | `af21f1d69909dae65ab5005ad324916db83063142a444aef856acf6f9dca1860` |
| Android/AI-Image-Editing-Tools.apk | 48461142 bytes | `4165ff73b304ffdd3baaf49589ba3acb89573a083a5231c74684a91f4cc4f9e6` |

APK signer SHA-256: `4a6e6b133417dd34f3c4b357590e7500f7d92daada25b2f39621ab7b598f1508` (unchanged).

# 1.2.1 图片混淆简化与次数 / Scramble rounds and simpler workflow

本地待检查版本，未上传。默认 Gilbert、系数 1.0、一次混淆与还原、无损 PNG、原尺寸，颜色调整关闭。导入后直接生成；新增分别输入 1–20 次，次数候选展示预览后确认。网页保留工具箱配色，更多设置默认收起，处理区按功能排成等宽三列。混淆页面去掉其他算法、源文件附件、私有参数记录和压缩模拟；其他工具不受此次精简影响。三端说明、教程与 README 同步更新。

Local review build; not uploaded. Defaults: Gilbert, shift 1.0, one scramble/restore round, lossless PNG, original dimensions and color adjustments off. Import and generate. Separate 1–20 round inputs and confirmed restore candidates are added. The web retains toolbox colors, collapses advanced settings and groups equal three-column actions. Other scramble algorithms, source attachments, private records and compression simulation are removed from this page. Other tools remain independent. Guides and READMEs are synchronized.

576 核心逐像素对照、10 组候选排序、网页与 Windows 双向三次 PNG 还原、完整 Windows 界面回归通过。APK 沿用原签名。平台发布审核通过率与 Android/iOS 真机未验收；不声称 100% 通过。详情见 [验证范围](VALIDATION.md) / [validation](VALIDATION.en.md)。

| Package | Size | SHA-256 |
| --- | ---: | --- |
| Windows/AI-Image-Editing-Tools.exe | 2309120 bytes | `75d8ab263aabdfc7fbd8a8c073f7944b725891c101db657ad61dd634342fce42` |
| Android/AI-Image-Editing-Tools.apk | 48461142 bytes | `e1f4c24fafcc333bfb72e4027f9500908466e1f0ea3a11e1630c2466f34830fe` |

APK signer SHA-256: `4a6e6b133417dd34f3c4b357590e7500f7d92daada25b2f39621ab7b598f1508` (unchanged).

# 1.2 历史本地候选 / Previous local candidate

2026-10-13 · 本地待检查 / Local review, not uploaded

本轮复查补齐三端算法说明、相关参数显隐与默认自动读取；提供默认参数。网页按钮按功能排为三列，区分当前、勾选、全部，压缩模拟说明位于底部。

UI review adds algorithm guidance, relevant controls, default automatic reading and default settings on all platforms. Web actions use three columns for current, checked and all, with explained compression simulation at the bottom.

新增四种统一混淆模式、独立移位系数和编码质量、原版 PicEncrypt 兼容、参数记录与候选确认、压缩模拟，以及本机处理的独立双语 HTML。保留旧 Gilbert 和原文件恢复格式，Android 拆出混淆控制逻辑，原 AI 推理和原生库保持一致。Windows / Android 教程、README、功能对照、算法说明与验证限制同步更新。

Adds four matching scramble algorithms, separate shifts and quality, original PicEncrypt compatibility, recorded parameters, confirmed candidates, compression simulation and a standalone local bilingual HTML. Legacy restoration remains supported. Android controls are extracted from the main page, and native AI libraries are byte-identical to the previous package.

136 组核心互通测试、72 组原版源码外部对照、54 组持久兼容向量通过；Windows 与网页版四种模式双向全 RGBA / 原文件字节恢复通过。手机系统选图、拍照、Android/iOS 真机和大图性能仍需复测。详细记录见 [验证范围](VALIDATION.md) / [validation](VALIDATION.en.md)。

未推送、未上传 Release、未部署 Pages；用户确认后再发布。Pages 工作流仅手动触发，产物只包含 `obfuscation/`。配置采用 [GitHub Pages 官方工作流方式](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages)。

| Package | Size | SHA-256 |
| --- | ---: | --- |
| Windows/AI-Image-Editing-Tools.exe | 2307072 bytes | `c6e124443b4bd850731f2287b815eedae07982209f09e26983726cdf0c655ba7` |
| Android/AI-Image-Editing-Tools.apk | 48465238 bytes | `a94d7c91b55ec0f62e19cd0497d569bd233ea437d1b23749d704bf50996a14fc` |

APK signer SHA-256: `4a6e6b133417dd34f3c4b357590e7500f7d92daada25b2f39621ab7b598f1508` (unchanged).

---

## 以前版本记录 / Previous release records

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

Locator points can remain visible and naturalness does not yet match every reference. Windows CUDA and Android CPU differ in output and speed. SDXL is experimental; physical ARM64 performance and compressed QQ / WeChat transfers are not comprehensively verified. Compatibility is not guaranteed for every model, selected region or phone released since 2024. See [Validation](https://github.com/Penumbrill/AI-Image-Toolbox/blob/main/docs/VALIDATION.en.md) and [Platform comparison](https://github.com/Penumbrill/AI-Image-Toolbox/blob/main/docs/PLATFORM_PARITY.en.md).

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

定位点仍可能可见，融合自然度尚未达到所有参考案例。Windows CUDA 与 Android CPU 的效果和速度存在差异；SDXL 为实验备用，ARM64 真机性能及 QQ / 微信压缩传输尚未全面验证。不能保证任意模型、任意选区或所有 2024 年以后上市的手机均可用。详见 [验证范围](https://github.com/Penumbrill/AI-Image-Toolbox/blob/main/docs/VALIDATION.md) 与 [两端功能对照](https://github.com/Penumbrill/AI-Image-Toolbox/blob/main/docs/PLATFORM_PARITY.md)。

## Downloads and integrity / 下载与完整性

Download the two packages from this release's Assets. / 请从本次发布的 Assets 下载两个安装包。

| Package / 安装包 | Size / 大小 | SHA-256 |
| --- | ---: | --- |
| `AI-Image-Editing-Tools.exe` | 2,271,232 bytes (2.17 MiB) | `8f11a287c07d57b668543d106dc1716c34a21d6d542868b10c2f66e41a5b818b` |
| `AI-Image-Editing-Tools.apk` | 48,436,566 bytes (46.19 MiB) | `f85a352b4d70186ac1da07cf483dccac3c42dcb0021958729bbf1e29fa3cd865` |

APK signer certificate SHA-256 / APK 签名证书 SHA-256：`4a6e6b133417dd34f3c4b357590e7500f7d92daada25b2f39621ab7b598f1508`

[MIT License](https://github.com/Penumbrill/AI-Image-Toolbox/blob/main/LICENSE). Third-party code and models retain their own licenses; see [Third-party notices](https://github.com/Penumbrill/AI-Image-Toolbox/blob/main/THIRD_PARTY_NOTICES.md) and [Android native notices](https://github.com/Penumbrill/AI-Image-Toolbox/blob/main/android/native/THIRD_PARTY_NOTICES.md).
