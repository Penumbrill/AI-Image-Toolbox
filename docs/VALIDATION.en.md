# Validation scope

English | [简体中文](VALIDATION.md) | [Home](../README.en.md) | [Packages and release notes](RELEASES.md)

Updated: 2026-10-12. These checks and limitations apply to the current formal EXE / APK. Implemented features do not imply validation on every device, image or model.

## Windows

- The production Windows UI passed core and complete bilingual regression across 11 pages, covering actual controls, language switching with parameter retention, batch export/restoration, redaction sessions, QR import/export and linkage, text layers and preview placement.
- Automatic captioning remains visible during manual placement and permits prompt editing when disabled. At most one placement option can be checked, or both can be off. Empty-preview import, clearing and model/parameter entry points passed UI regression.
- Numeric wheel input, source-color strength/reset, transparent PNGs and background-reveal pixel/decoder regressions passed. Shared vectors compare C# and JVM color processing.
- Media Foundation capture opened an actual ASUS FHD webcam twice, acquiring four 640×480 frames each time and reopening after release. This does not certify other cameras, infrared devices or all optical scans.
- The default SD 1.5 desktop fusion pipeline has actual generation and scan checks. SDXL testing has not established comparable stability, so it remains experimental; custom model quality is not guaranteed.

## Android ordinary tools and UI

- An API 35 x86_64 emulator passed core and complete ordinary-tool UI regression, including empty-preview import, arbitrary nonadjacent queue subsets, batch processing, RGBA / HEX colors, source-proportional file covers, redaction undo and full-canvas preservation.
- GIF playback, stop, looping and non-looping completion, WebUI / ComfyUI parameter reading and Copy all, 64-bit seeds and persistent numbering/size/GIF defaults passed.
- Actual saved/shared output bytes matched. Gallery and authorized-directory entry points use real system Intents. QQ round-trip reports compare two packaged files, but local comparison does not replace actual QQ compression or transfer tests.
- Independent prompt/placement controls, at most one placement option or both off, hidden positioning previews during avoidance, clearing, model selection and saved parameters passed regression. Across 44 page switches, median latency was 33 ms and p95 was 35 ms.
- The formal APK was installed and checked through actual touches reaching QR, model setups/downloaded-file listings, settings and tutorials. Installed bytes match the public APK; test classes are excluded from the formal package.

## QR import and model management

- Actual QQ colored circular-dot and WeChat logo screenshots were each 1206×2622. Production import detected one code in each; a re-encoded 512-pixel standard QR decoded to the same payload. Source screenshots were unchanged. Account contents, original screenshots and private paths are excluded from public files.
- Production Android model management passed 16 host regression groups using a real local HTTPS service: certificates and refusal of HTTP downgrades, resume/restart, bad ranges/truncation, wrong hashes, cancellation, tail tampering, original-location reuse, duplicate import, completion gating and managed-cleanup safeguards.
- Aggregate progress stays at most 99% until every required resource is verified. Directory-junction escape rejection passed; a file-symlink case could not be created with host permissions and was not completed. Host checks do not certify phone mirror throughput or switching between physical storage volumes.

## Actual offline Android AI

- `AiVision` uses pinned real BLIP, SegFormer and anime-face ONNX weights, without replacing models with skin-color thresholds. Host inference on three complex images and nine prompt comparisons passed. Four protection/material arrays for the same 1920×1280 image matched Android emulator and host outputs byte for byte.
- Real arm64-v8a and x86_64 diffusion libraries were built. The x86_64 emulator loaded pinned libraries and completed lifecycle checks, one-step 128×128 ControlNet diffusion and cancellation during encoding. Small runs do not establish full fusion quality.
- The final complete production run used an API 35 x86_64 emulator with 8 GiB configured RAM, airplane mode on and Wi-Fi/cellular disabled. It executed real captioning, segmentation, face protection and DreamShaper 8 / QR Monster SD 1.5 diffusion.
- Work resolution was 384, with 24 configured steps, CFG 5.5, denoise 0.42 and ControlNet 0.8. Manual x=20%, y=75%, size=38% retained the full 1920×1280 output and requested region x=159, y=735, side=451. All outside pixels and 486,477 protected source pixels were preserved.
- Direct full-image decoding of the first native candidate failed; reduced decoding was short-circuited and not attempted. After local circular tonal correction, Android ZXing and independent ZXing-C++ confirmed the same payload at full size and 900×600. Total time was 770,402 ms, about 12 minutes 50 seconds. Switching between Settings and QR remained possible while the task continued.
- Visual inspection found the original blue building and brown/pink foliage palette, without a square white/blurred backing plate. Circular points and some light separators remain visible, below some reference images in naturalness. This result covers one image and one manual position.

## Documentation and packages

- Both READMEs match the tutorials shared by both clients across 119 paragraphs per language, including 29 QR paragraphs. JSON, public paths, private-file exclusions and local Markdown links passed checks.
- APK ZIP integrity, eight native-library identities, 16 KiB alignment and original-certificate v1/v2/v3 signatures passed. Six EXE embedded resources match current source bytes. Current sizes and SHA-256 values are in [Release notes](RELEASES.md).
- Retained regression sources are in `tests`, `android/tests` and `tools`; build entry points are documented in [Building](BUILD.en.md). Build caches, test packages, private images and duplicate per-round process records are excluded from the public directory.

## Not comprehensively verified

Physical ARM64 speed and peak memory, ordinary-tool regression on 32-bit Android devices, more complex images/manual positions, mobile SDXL, other optical cameras and compressed QQ / WeChat transfers require separate tests. Windows CUDA and Android CPU do not guarantee identical naturalness, nor can every phone released since 2024 be certified. See [Platform comparison](PLATFORM_PARITY.en.md) for implemented features and differences.
