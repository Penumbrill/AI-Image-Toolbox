# Build and test

English | [简体中文](BUILD.md) | [Home](../README.en.md)

## Windows

Use Windows with the .NET Framework 4.x C# compiler, PowerShell and Python 3.10+. .NET Framework 4.8 is recommended for running the app. No NuGet packages are needed.

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -Test
python tests/windows/verify_apng.py
python tests/windows/verify_tools.py
python tests/windows/verify_inspection.py
python tests/windows/verify_obfuscation.py
```

Tests use `build/windows/AI-Image-Editing-Tools.exe` and write to `build/verification/`. Only `-Test` includes developer self-tests. Run the four Python checks in this order: later checks reuse earlier synthetic fixtures.

Build the release without self-tests:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./build.ps1 -OutputDirectory releases/Windows
```

The icon is included. `build-icon.ps1` regenerates the required ICO without preview files.

## Android

Install Python 3.10+, JDK 17+ and the official Android SDK packages `platforms;android-35` and `build-tools;35.0.0`. Gradle is not required. The APK now bundles native diffusion and ONNX Runtime, so prepare the pinned dependencies below first. Native compilation additionally needs Python 3.11+, Git, the Android NDK and CMake. Keep development dependencies outside the public repository; users do not need the SDK or NDK. Ordinary tools work offline, and AI works offline after its missing models have been downloaded.

### Native diffusion and recognition dependencies

1. Prepare stable-diffusion.cpp and its GGML submodule at the commits in [source-lock.json](../android/native/source-lock.json), NDK `30.0.16248370`, CMake 3.22+ and matching Ninja. The pinned backend is CPU. arm64-v8a / x86_64 provide Android 7+ AI; armeabi-v7a / x86 provide only Android 5+ ordinary-tool installation compatibility.
2. Set `AIPT_SD_SOURCE`, `AIPT_NDK_ROOT`, `AIPT_CMAKE_BIN` and `AIPT_NATIVE_OUTPUT`, then run `python android/native/build_native.py`. It verifies source/NDK versions, builds two diffusion libraries and two ordinary-tool compatibility libraries, and writes `build-report.json`. It neither downloads nor runs models.
3. Download the official ONNX Runtime Android 1.23.2 AAR from the Maven URL in [onnxruntime.lock.json](../android/native/onnxruntime.lock.json), verify its bytes/SHA-256 and extract it outside the repository. The extracted root must contain `classes.jar` and ONNX libraries under `jni/arm64-v8a` and `jni/x86_64`. APK packaging rechecks pinned extracted-file sizes and hashes, rejecting mismatched dependencies.
4. Point `AIPT_NATIVE_LIB_DIR` at the native output's `jni` folder and `AIPT_ONNXRT_DIR` at the verified AAR extraction root. Full licenses are in [Android notices](../android/native/THIRD_PARTY_NOTICES.md). The APK includes [android-ai-models.json](../assets/android-ai-models.json), while large model weights are not bundled.

Set `ANDROID_SDK_ROOT` and `JAVA_HOME`, or overrides `AIPT_SDK_ROOT` and `AIPT_JAVA_HOME`. Use the standard SDK layout:

```text
<SDK>/platforms/android-35/android.jar
<SDK>/build-tools/35.0.0/
```

```sh
export ANDROID_SDK_ROOT=/path/to/android-sdk
export JAVA_HOME=/path/to/jdk-21
export AIPT_NATIVE_LIB_DIR=/path/to/aipt-build/native/jni
export AIPT_ONNXRT_DIR=/path/to/aipt-build/onnxruntime-1.23.2
python android/build.py
```

The release is written to `releases/Android/AI-Image-Editing-Tools.apk`. `python android/build.py --test` builds `build/android/test.apk`, including test classes and the unpacked fixture bundle. Install and start its self-test using adb:

```sh
adb install -r build/android/test.apk
adb shell am start -n com.guangyingruoshui.imagetools/.MainActivity --ez selftest true
```

Each build has an isolated intermediate directory. Release assets contain bilingual tutorials, the model manifest and notices, without test fixtures or self-test entry points. Native libraries are aligned for 16 KiB pages. Signing verification succeeds before the deliverable is replaced. The Android version is 1.1 / versionCode 2, with minSdk 21 for ordinary tools and API 24+ for AI runtimes.

## Signing

`AIPT_KEYSTORE` and `AIPT_KEYSTORE_PASS_FILE` explicitly select an existing key/password and take priority, with alias `guangyingruoshui`. Without overrides, an existing `~/.ai-image-tools/signing/image-editing-tools/release.jks` selects that project-specific backup and its adjacent `password.txt`; otherwise the builder falls back to `~/.ai-image-tools/signing/`. It creates a developer identity only when no key or orphaned password exists. Retain an existing generic directory rather than replace the author's identity with another project's key. On Windows, the project backup is under `C:/Users/<username>/.ai-image-tools/signing/image-editing-tools/`, outside the disposable workspace.

Repository-contained keys/passwords are rejected. An APK signed with a new key cannot upgrade the author's APK; verify the signer certificate before release rather than infer identity from a matching filename.

The author's existing key is stored outside this repository. Back it up and reuse it for future updates; never publish it or its password.

## Upload

Upload sources, documentation and the two latest packages to one repository. Do not include `build/`, Python caches, SDKs or signing files. Automated checks inspect filenames, private files and release-package structure. CI does not publish packages or commit updated binaries.

## QR dependencies and new checks

Windows embeds ZXing.Net 0.16.11; Android compiles ZXing core 3.5.4. Pinned libraries, SHA-256 and the full Apache-2.0 license are in [vendor/zxing](../vendor/zxing/README.md). No runtime dependency downloads.

That statement applies to these fixed QR libraries: the Windows AI runtime/dependencies and AI models on both platforms are prepared on demand. Android bundles diffusion/recognition libraries; phones do not install Windows Python, PyTorch or CUDA.

`--self-test` includes QR/border/opaque-linkage checks, grayscale isolation, text layers and history. `--ui-test` covers bilingual layouts and QR export/import. Android `--test` compiles corresponding native bitmap/touch checks; compiling them does not count as running device tests. Camera capture needs separate physical-device validation.

## Scramble and standalone-web tests

Build the test EXE first. Set `AIPT_JAVA_HOME`, `AIPT_SDK_ROOT` and optionally `AIPT_NODE`, then run:

```powershell
python tests/windows/verify_scramble_options.py
python tests/web/create_check.py
python -m http.server 8765 --bind 127.0.0.1
```

Open `http://127.0.0.1:8765/build/web-check.html` and run the visible local check. Test controls are generated under ignored `build/` only; the public `obfuscation/index.html` has no test entry. `AIPT_VERIFICATION_DIR` can select a fresh Windows regression output folder to avoid stale AI storage fixtures. The Pages workflow publishes only `obfuscation/` after changes to that directory on main, or a manual trigger. Validate the local page before pushing.
