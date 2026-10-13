# 构建与测试

[English](BUILD.en.md) | [返回首页](../README.md)

## Windows

需要 Windows 的 .NET Framework 4.x 编译器、PowerShell 和 Python 3.10+；程序建议使用 .NET Framework 4.8。固定版本二维码库已包含在 `vendor/zxing/`，无需构建时下载 NuGet 包。在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Test
python tests/windows/verify_apng.py
python tests/windows/verify_tools.py
python tests/windows/verify_inspection.py
python tests/windows/verify_obfuscation.py
```

测试使用 `build/windows/AI-Image-Editing-Tools.exe`，结果存放在 `build/verification/`。`-Test` 才编译开发自测代码。四个 Python 检查须按上面的顺序执行，后续检查使用前面生成的合成样本。

正式包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -OutputDirectory releases/Windows
```

图标已随仓库提供；需要重新生成时运行 `build-icon.ps1`。只生成必要 ICO，不生成预览图片。

## Android

需要 Python 3.10+、JDK 17 或以上、官方 Android SDK `platforms;android-35` 与 `build-tools;35.0.0`。构建不依赖 Gradle。APK 现在包含原生扩散与 ONNX Runtime，需要先准备下列固定版本依赖；原生编译脚本另要求 Python 3.11+、Git、Android NDK 和 CMake。开发依赖不放进公开仓库；用户不需要下载 SDK 或 NDK。普通工具离线运行，AI 首次需要下载所缺模型，准备后可离线推理。

### 原生扩散与识图依赖

1. 按 [source-lock.json](../android/native/source-lock.json) 准备对应 commit 的 stable-diffusion.cpp 与 GGML 子模块、NDK `30.0.16248370`、CMake 3.22+ 和配套 Ninja。锁定的是 CPU 后端，arm64-v8a / x86_64 提供 Android 7+ AI，armeabi-v7a / x86 仅提供 Android 5+ 普通工具安装兼容。
2. 设置 `AIPT_SD_SOURCE`、`AIPT_NDK_ROOT`、`AIPT_CMAKE_BIN`、`AIPT_NATIVE_OUTPUT`，执行 `python android/native/build_native.py`。脚本核对源码和 NDK 版本，生成两份扩散库与两份普通工具兼容库、写入 `build-report.json`；不下载或运行模型。
3. 从 [onnxruntime.lock.json](../android/native/onnxruntime.lock.json) 指定的官方 Maven URL 下载 ONNX Runtime Android 1.23.2 AAR，先核对文件大小及 SHA-256，再解压到仓库外的目录。该目录应包含 `classes.jar` 与 `jni/arm64-v8a`、`jni/x86_64` 下的 ONNX 运行库。APK 构建再次核对解压文件的固定大小和 SHA-256；不兼容或改写的依赖会拒绝打包。
4. `AIPT_NATIVE_LIB_DIR` 指向原生输出中的 `jni` 目录；`AIPT_ONNXRT_DIR` 指向已校验 AAR 的解压根目录。完整运行库版权见 [Android notices](../android/native/THIRD_PARTY_NOTICES.md)。模型清单 [android-ai-models.json](../assets/android-ai-models.json) 随 APK 提供，但大模型权重不随包打入。

设置 `ANDROID_SDK_ROOT` 和 `JAVA_HOME`，或者使用覆盖变量 `AIPT_SDK_ROOT`、`AIPT_JAVA_HOME`。使用标准 SDK 布局：

```text
<SDK>/platforms/android-35/android.jar
<SDK>/build-tools/35.0.0/
```

```powershell
$env:ANDROID_SDK_ROOT = 'C:/Android/Sdk'
$env:JAVA_HOME = 'C:/Java/jdk-21'
$env:AIPT_NATIVE_LIB_DIR = 'D:/aipt-build/native/jni'
$env:AIPT_ONNXRT_DIR = 'D:/aipt-build/onnxruntime-1.23.2'
python android/build.py
```

正式包输出至 `releases/Android/AI-Image-Editing-Tools.apk`。`python android/build.py --test` 生成 `build/android/test.apk`，包含测试类与解压后的小型样本。使用 Android SDK 的 adb 安装测试包后启动自测：

```sh
adb install -r build/android/test.apk
adb shell am start -n com.guangyingruoshui.imagetools/.MainActivity --ez selftest true
```

每次构建使用独立的中间目录，避免旧测试资产混入正式包。正式 assets 包含双语教程、模型清单与版权声明，不带测试样本和自测入口；原生库按 16 KiB 对齐，签名验证成功后才替换交付文件。Android 包版本为 1.1 / versionCode 2，普通工具 minSdk 21，AI 运行库要求 API 24+。

## 签名密钥

`AIPT_KEYSTORE`、`AIPT_KEYSTORE_PASS_FILE` 可显式指定已有密钥和密码文件，优先于默认路径；别名为 `guangyingruoshui`。未指定时，若用户主目录 `.ai-image-tools/signing/image-editing-tools/release.jks` 已存在，则使用此项目专用备份及同目录 `password.txt`；否则回退 `.ai-image-tools/signing/`，只有缺少密钥且没有遗留密码文件时才创建开发者自己的身份。保留原有通用目录，不用另一项目的身份覆盖作者身份。Windows 默认备份路径位于 `C:/Users/<用户名>/.ai-image-tools/signing/image-editing-tools/`，独立于待清理工作区。

脚本拒绝把密钥或密码放在仓库内。新密钥签名的 APK 不能覆盖安装作者签名的 APK；发布前须核对签名证书，不以文件名相同认定身份一致。

作者原有签名密钥已保存在仓库之外，后续更新需继续使用它；请备份，不要上传。不要把密码粘贴到命令或公开日志中。

## 上传

源文件、两端最新安装包和说明放在同一个仓库即可。`build/`、`__pycache__/`、本地 SDK、签名文件不上传。自动检查验证文件名、隐私文件和正式包结构。GitHub CI 不发布安装包、不提交构建后的二进制文件。

## 二维码依赖与新增检查

Windows 内嵌 ZXing.Net 0.16.11，Android 编译 ZXing core 3.5.4。固定库、SHA-256 与完整 Apache-2.0 许可见 [vendor/zxing](../vendor/zxing/README.md)，运行时不下载依赖。

该“运行时不下载”仅指上述固定二维码库；Windows AI 环境/依赖和两端 AI 模型按需准备。Android 的扩散/识图库已随 APK 提供，手机不安装 Windows Python、PyTorch 或 CUDA。

`--self-test` 包含二维码、白边、联动不透明区、封面灰度隔离、多文字和历史检查；`--ui-test` 包含新页面中英文布局及导出/导入。Android `--test` 编译对应原生位图与触摸检查，只有在真机或模拟器执行才算通过；相机需要实际设备单独测试。

## Scramble and standalone-web tests

Build the test EXE first. Set `AIPT_JAVA_HOME`, `AIPT_SDK_ROOT` and optionally `AIPT_NODE`, then run:

```powershell
python tests/windows/verify_scramble_options.py
python tests/web/create_check.py
python -m http.server 8765 --bind 127.0.0.1
```

Open `http://127.0.0.1:8765/build/web-check.html` and run the visible local check. Test controls are generated under ignored `build/` only; the public `obfuscation/index.html` has no test entry. `AIPT_VERIFICATION_DIR` can select a fresh Windows regression output folder to avoid stale AI storage fixtures. The Pages workflow publishes only `obfuscation/` after changes to that directory on main, or a manual trigger. Validate the local page before pushing.
