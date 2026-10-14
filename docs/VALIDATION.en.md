## Mobile web update validation (2026-10-14)

- Packed paths match the independent original across 576 dimensions; shifts, rounds, color operations and inversion remain identical. A 4032 × 3024 path uses 48,771,072 bytes without a coordinate array per pixel.
- Actual desktop Chromium checks pass: one native image picker, multiple selection, input reset, missing MIME, FileReader fallback, missing/rejected createImageBitmap fallback, JPEG EXIF orientation, PNG corruption, subsets, language switching, candidates and cancellation. A synthetic 4032 × 3024 JPEG passes decoding, Worker scrambling and full-sized PNG export. A real ZIP download contains two images with correct dimensions.
- At 390 CSS pixels: no horizontal overflow, three equal queue-action columns, 16px form text and a shorter empty queue.
- Physical iOS Safari, Android Chrome and in-app photo/download menus require user retesting. Desktop/viewport checks do not establish physical mobile compatibility. HEIC/HEIF support depends on the browser. The default ceiling is 16 MP, lowered to 4 MP on devices reporting low memory; limits report an error without resizing. The separate capture entry is removed; save new photos to the library before importing.
- Only web source, documentation, tests and deployment checks change; EXE/APK bytes and SHA-256 remain identical.

## 0.3.0 Validation

- Joint shift/round search: six samples with identical top-three candidates and scores in C#, actual Android Java and web code. Known permutations rank in the top three. Single-pixel and equivalent parameters remain ambiguous.
- Video: seven FFmpeg-generated tagged MP4/MOV/WebM/MKV cases include mdta, faststart, rotation and JSON attachments. ffprobe confirms tag removal and track properties; encoded-packet and decoded audio/video SHA-256 match before/after. Actual Java and C# outputs are byte-identical. Corrupt inputs and cancellation leave no output. FFmpeg/ffprobe are test dependencies only.
- Unbounded unknown-size Clusters and unsupported encrypted/abnormal containers are rejected. The cleaner targets supported container metadata, not private data inside encoded streams.
- Android validation uses host-JVM algorithm/IO comparisons and signed builds, not a physical device; iOS web testing remains pending.

# Validation scope

English | [简体中文](VALIDATION.md) | [Home](../README.en.md) | [Packages and release notes](RELEASES.md)

Updated: 2026-10-13. These checks and limitations apply to the current formal EXE / APK. Implemented features do not imply validation on every device, image or model.

## 1.2.3 cover-only trial (2026-10-13)

- Optional one-round cover scrambling uses shift 1.0 and no color adjustments. Text and numbering are added afterward. Hidden content and file attachments are not permuted.
- Two actual dual-image exports restore exact pixels; two samples per four packaging modes restore exact file bytes. Whole-image scrambling and static re-encoding lose dynamic frames/attachments and are correctly rejected as unrecoverable.
- The actual Android CoverScramble class matches Windows pixels across six dimension/alpha cases on the host JVM. Cancellation leaves the bitmap unchanged. Bitmap/IO wrappers are test stubs; this is not device validation.
- Complete Windows UI regression passes: default-off behavior, changed current dual-image cover with byte-identical hidden content, real batch file restoration and bilingual layout. APK version 1.2.3 / code 6 retains its signing certificate.
- The user reports successful Bilibili publication without blocking for ordinary scrambled images. This is feedback for that sample, without measuring general acceptance. Dual-image and file-cover trials still require platform testing.
- No additional uploads, pushes or deployment. Local synthetic samples contain no private data.

## 1.2.2 local review build (2026-10-13)

- Windows destination actions pass real clicks, two-round batch restoration and full UI regression, including language switching and layout.
- Web processing/download actions precede collapsed settings; desktop and 390 px three-column widths pass checks.
- Bundled guides and bilingual README structure/location instructions are synchronized. Ordinary copy omits creator displays and external algorithm-site descriptions; license texts retain required copyrights.
- Algorithm function bodies are unchanged from the verified snapshot; only comments and layout were reorganized. The 576 comparisons and cross-platform restoration results below still apply.
- APK version 1.2.2 / code 5 retains its signer. Physical Android/iOS devices remain untested. Packages/checksums remain local, without upload.

## 1.2.1 local review build (2026-10-13)

- All platforms default to Gilbert, shift 1.0, one scramble/restore round, lossless PNG, original dimensions and color adjustments off. The current scrambling workflow emits no source attachment or private parameter record.
- 576 pixel comparisons against executed external source pass: 21 coefficients, both directions, 1/2/3/7/20 rounds, odd/thin sizes, alpha channels and green-adjustment order. Original-function test snapshot SHA-256: `9e7b58931a2531d8a7c20bacf6f1ae5d944f4747283313cfc871adc955123366`. Engine arrays include arbitrary alpha; platform encoding bytes can differ.
- Ten candidate rankings have identical scores/order in C#, actual Android Java classes and web code. A known twice-scrambled gradient is in the top three. Degenerate images still require confirmation and cannot uniquely reveal a count.
- Five actual three-round Windows PNG round trips match pixels; 1280 x 1920 output preserves dimensions. PNG ends at IEND with no private record. Web Canvas PNG and Windows restore each other's three-round images. The actual downloaded browser ZIP passes CRC and five client pixel restorations.
- Desktop browser checks pass for import/defaults, four rows of three equal buttons, JPEG quality visibility, corrupt PNG refusal, subset/all/empty selection, Worker cancellation, language preservation, three candidate previews and explicit confirmation. Transparent PNG alpha is preserved; Canvas RGB rounding is retained, without promising recovery of hidden transparent RGB bytes.
- Complete Windows self-tests and UI regression pass, including a real two-round batch button restoration, matching preview/export sizes, dual-image, background reveal, file packaging, redaction, tags and QR UI. AI inference was neither changed nor revalidated this update.
- APK builds as 1.2.1 / code 4 with the existing signing certificate. Formal packages exclude test classes and new test commands. Desktop/narrow-screen web layout checks are saved locally. No physical Android/iOS device: system pickers, camera, actual mobile downloads and device memory behavior need retesting.
- The user confirmed Bilibili saved a resized preview; loading View original before saving restored successfully. Read-only/local validation did not test publishing acceptance rates and makes no approval guarantee.
- No upload, push, Release or Pages deployment. Local packages and HTML are provided for user review first.

The following 1.2 records describe the previous local candidate. Its four algorithms, source recovery and private parameters do not describe the present scrambling UI.

## Local validation on 2026-10-13

- 136 pixel permutations and inverses match across C#, Android Java and JavaScript, covering all algorithms, row variants, keys, strengths, odd/thin/single-pixel images and irregular block edges.
- Executed original PicEncrypt Java source for 72 compatibility cases; 54 external vectors are retained in `tests/fixtures/scramble-external.json` for reproducible checks.
- Windows and web restore each other's four algorithms, matching all RGBA bytes including transparent colors, recorded parameters and SHA-256 of original files. Legacy defaults and original-file records continue to pass.
- Full Windows self-tests and independent APNG, metadata, redaction, tag and scramble checks pass, including batch/cancel/UI and existing image/QR structures. AI inference code was retained; large-model inference was not repeated in this run.
- The actual desktop browser passes Worker, PNG CRC, external vectors, original recovery, subsets, cancel, language switching and ZIP downloads. Python independently verifies ZIP CRC and Chinese/duplicate filenames. A 390-pixel responsive viewport has no horizontal overflow.
- JPEG 85/75 and 75%/50% resize samples were actually generated. One synthetic transparent PicEncrypt sample had restored RGB mean errors 78.67 / 79.94 (0–255). Resizing changes permutation dimensions. These results are sample-specific, not platform guarantees.
- One ordinary QR fixture retains its full payload after JPEG 85/75 and 75%/50% resizing (four PASS results); this does not certify every AI QR or platform.
- APK compilation, signature verification and original signer checks pass. Android algorithms ran on a host JVM; no Android or iOS physical device was available for bitmap decoding, picker/camera, navigation, cancellation or download verification. Automated system file selection was not completed. Large-image performance still requires target-device testing.
- Nothing was uploaded, pushed, released or deployed. A manual Pages workflow is prepared locally and publishes only `obfuscation/`, after user review.

See [algorithm choices](SCRAMBLE_ALGORITHMS.md). Earlier records below are historical coverage, not new physical-device or AI inference validation.


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

## Parameter UI and web layout review

The complete Windows UI regression passed, including algorithm control visibility, translated descriptions and automatic reading enabled by default. Browser checks passed default settings, current/checked/all scopes, cancellation, 54 external vectors and lossless recovery. The 390 px viewport keeps equal three-column action rows without horizontal overflow. Android UI compiles; physical devices remain untested. 50 size/coefficient combinations matched the directly executed baseline curve and pixel permutation. No Bilibili uploads or approval-rate measurements were performed.

Android CI recompiles Java and packages APKs using shipped native libraries plus verified pinned ORT. Native source changes require a separate `android/native/build_native.py` rebuild.

Account synchronization (2026-10-14): project links and the default QR URL use Penumbrill. Changes affect links, copy and embedded release documentation; algorithms, Android application ID and signing certificate remain unchanged.
