# Windows / Android feature comparison

English | [简体中文](PLATFORM_PARITY.md) | [Home](../README.en.md) | [Validation](VALIDATION.en.md)

Updated: 2026-10-12. This table describes actual processing entry points in the current source. Package versions and completed checks are recorded in [Releases](RELEASES.md) and Validation. Implemented features do not imply testing on every phone, model or third-party service.

## Image tools and shared operations

| Feature | Windows EXE | Android APK |
| --- | --- | --- |
| Dual image and restoration | Animated PNG, paired covers, text layers, numbering, batch export/restoration | Same purpose and processing entry points; system file picker for import |
| Background reveal | Monochrome/color, five methods, custom effective strength, actual white/black previews and QR linkage | Same purpose and choices; third-party transparency rendering still needs testing |
| Scramble/restoration | Original-file attachment, lossless pixels and JPEG; batches and actual file size | Same three modes; default 1200-pixel working canvas, optional 2048 or original; original-file mode still retains complete source bytes |
| File disguise | JPEG, copy/b, PNG ZIP and IDAT ZIP, video/archive restoration | Same four containers and restoration; selected cover keeps its aspect ratio, default edge 1600 with 1200/2048/original alternatives and proportional cover-budget reduction |
| Manual QQ round-trip report | Choose sent/received files, check size/hash, record channel and export report | Same actual comparison/report purpose; it does not establish untested QQ transfer methods |
| GIF | Queue, delay, size, looping and Play/Stop preview | Same choices; delay 20–10000 ms, edge 100–2048 px; playback stops on hidden pages |
| Clean info | PNG/APNG/JPEG/WebP/GIF metadata cleanup and batches | Same format entry points; creates new files and retains sources |
| Redact | Rectangle/brush, mosaic/blur/solid fill, arbitrary colors, undo/redo | Same purpose; touch zoom/pan and pause after scrolling; selectable working size and complete working-canvas output |
| Read tags | WebUI/ComfyUI panels, read both from one image, field/all copying, TXT | Same operations; reads stored metadata rather than generating images from captions |
| Queue/batch | Current row, arbitrary subset, select/clear all, ordering and removal | Check any subset; batch uses checks first, otherwise all; Export current uses the current row |
| Text/colors/defaults | Text layers, positions/dragging, colors; numbering, size and GIF defaults | Text layers, hold-to-position, arbitrary RGBA/HEX colors; persistent numbering, size and GIF delay/size/loop defaults |
| Empty-preview import | Click picker or drag desktop files | Tap empty preview to invoke the real system picker; touch import replaces desktop drag/drop |
| Save/share | Root and feature subfolders, Explorer location opening | Album or authorized folders, feature subfolders and system sharing; custom-folder viewing depends on the file manager |
| Bilingual guide | Embedded Chinese/English, chapter links and offline reading | Same bilingual guide assets, chapter links and offline reading |

Queues, edit sessions and previews are not permanent projects; export results you need to keep. Resizing, screenshots, re-encoding and alpha changes can break animated switching, attachments or QR linkage.

## QR codes, offline AI and model management

| Feature | Windows EXE | Android APK |
| --- | --- | --- |
| Standard QR/screenshot import/camera scanning | General QR, QQ/WeChat screenshot extraction, multiple-code choice and white/black scanning | Same purpose; decoded content excludes avatars/names; proprietary codes cannot be presented as standard QR |
| Image overlays/pixel texture fusion | Size/coordinates, ordinary QR placement illustration and dragging | Same position controls, hold-to-drag and full-image output; pixel mode needs no models |
| Local AI embedded QR | Local Python/PyTorch/Diffusers with matching QR ControlNet | Native CPU diffusion and ONNX recognition, genuinely offline without forwarding to a computer |
| Manual/automatic position | Manual coordinates first; choose automatic avoidance before generation or after a failed result | Same choices; at most one placement option or both off; automatic placement hides ordinary QR illustration and position controls |
| Automatic prompts/manual editing | Independent caption switch; disable to edit, with automatic prompts also available during manual placement | Same logic; BLIP, segmentation and face models infer on the device |
| Model/parameter profiles | Default SD 1.5; alternative SDXL/custom models; templates and parameters saved per model | Default SD 1.5; experimental high-memory SDXL; same-family complete safetensors/GGUF, profiles saved per model |
| Scan validation/retries | Exact payload at full/thumbnail scales; CUDA refinement with sufficient VRAM | Validates native candidates and local luminance calibration; lacks desktop CUDA high-memory refinement |
| Complete image/preview/text | Retains the full composition; shows finished result, allows text afterwards and rechecks export | Same workflow; local native patches merge into the complete source, retaining protected source pixels |
| Download sources | Probes official/mirror reachability and response time; reachable mirror preferred for mainland system region | Probes official/mirror with system/carrier country hints; downloads only the selected setup |
| Background transfer/progress | File counts, overall bytes, size, speed and verification; other tools usable | Same information; separate background task and user-started foreground notification, with other tools usable |
| Reuse/integrity | Pinned size/SHA-256, bounded scans of existing locations; complete external models referenced read-only | Pinned size/SHA-256, direct reuse between managed locations; imports receive structural checks and fingerprints |
| Runtime/storage | EXE prepares private runtime first; defaults to a data-drive AI-Models folder, with custom paths | CPU diffusion/ONNX runtimes bundled in APK; models in sufficiently large app AI-Models storage, selectable app volumes |
| Actual paths/folder opening | Actual model/runtime paths and Explorer directory opening | Actual paths and in-app lists of files, sizes and fragments, with path copying |
| Delete/cleanup | Confirm selected managed model or managed runtime/model cleanup; external references protected | Confirm managed models/fragments/job-cache cleanup; uninstall removes bundled APK runtimes |

Phones have no Windows D/E/F drive rule and cannot install Windows Python/CUDA. Both clients provide corresponding paired-model, progress and prompt features, with platform-specific runtimes, import formats, permissions and performance policies; outputs are not promised to be pixel-identical. Mobile SDXL remains experimental, and desktop results cannot establish mobile verification.

## Phone requirements and verification limits

Ordinary tools support Android 5+; 32-bit ABI entries provide ordinary-tool compatibility only, with no completed 32-bit device regression this update. Offline AI requires a 64-bit Android 7+ OS. Capability checks use actual RAM, currently available memory and CPU, rather than a post-2024 release date.

Automatic policy uses 384 pixels below 10 GiB total RAM (320 remains selectable), otherwise 512 pixels, and up to four CPU threads by default. Before inference, SD 1.5 requires at least 3 GiB available system memory and SDXL 9 GiB. Other applications, OS restrictions and model differences can still cause failure; CPU generation time needs measurement on the phone.

The revised 384-pixel circular-point pipeline completed a new complex-image offline native run. Two decoders passed full/reduced scans, manual placement and the full canvas remained intact, and navigation stayed responsive during the approximately 12-minute-50-second run. The square white plate is removed; visible points remain below reference naturalness. Ordinary-tool core/UI regressions and actual formal-APK installation/navigation also passed. Physical ARM64, mobile SDXL and compressed transfer remain untested; see the [latest validation](VALIDATION.en.md).
