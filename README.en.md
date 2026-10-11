# AI Image Editing Tools

[English](README.en.md) | [简体中文](README.md)

A local Windows / Android image toolbox for dual-image PNGs, background reveal, pixel scrambling, file packaging, GIFs, metadata cleanup, redaction, generation-parameter reading and QR codes. No account is required and image processing does not upload files.

## Install and get started

Windows: download and run the [EXE](releases/Windows/AI-Image-Editing-Tools.exe). Android: download and install the [APK](releases/Android/AI-Image-Editing-Tools.apk), granting file or camera permissions when needed. The APK is not for iOS; iOS system cameras can scan the generated website QR codes.

Choose a tool in the left toolbox; on Android, open the menu for a scrollable tool list. Import inputs, check previews, choose a destination and export. Feature subfolders are created automatically; inputs remain unchanged and conflicting output names get unique alternatives. User guide below Settings contains the same instructions with offline chapter navigation. See [Windows / Android feature comparison](docs/PLATFORM_PARITY.en.md) for shared features and platform differences.

## Contents

1. [Dual image](#dual)
2. [Background reveal](#background)
3. [Scramble](#scramble)
4. [File disguise](#disguise)
5. [Make GIF](#gif)
6. [Clean info](#clean)
7. [Redact](#redact)
8. [Read tags](#tags)
9. [QR codes](#qr)
10. [Settings](#settings)

<a id="dual"></a>
## 1. Dual image

Store a cover and a second image in an animated PNG. Static previews show the cover; animation-aware viewers show the inner image. Whether opening a preview or loading the original switches the image depends on the platform.

### Steps

1. Import the images that should appear inside. Select a queue item and check both previews. Reordering changes export order and numbering.
2. Use an automatic cover or choose your own. Covers can differ for each item, or the current cover can be applied to other items.
3. Add multiple cover text layers as needed. Select a layer to change its text, size, color and position. Drag freely or enter horizontal and vertical percentages; delete layers individually.
4. Set numbering and output size, then export the current item or the queue. Send the generated PNG as an original file and test it on the intended platform.
5. To restore, import generated files into the lower restoration area, preview both images and export the cover or inner image. Background-reveal files are also recognized.

### Options and usage details

- Text size is a percentage of the short edge, and position is a percentage of the canvas. Layer names follow their text and abbreviate long entries; selecting a layer retains the others.
- Choose a longest edge of 1200, 2048 or 3200 pixels, or original size. Covers fit the canvas; inner images may be resized. Original size uses more memory for large files.
- Numbering is optional. Adjust the current item before applying its cover settings; batch output uses the actual pairings in the queue.
- Check any subset of queue items, select all or clear the checks. Batch export uses checked items first, otherwise the whole queue. Remove handles checks first, otherwise the current row. Export order follows the queue and actual cover pairings.

### Output, restoration and compatibility

Outputs use the Dual image feature subfolder. Restoration recovers the stored images, not pixels lost through resizing, original encoding or deleted animation frames. Conversion to a static image, screenshots or platform re-encoding can remove switching and restoration. This is not encryption.

<a id="background"></a>
## 2. Background reveal

Create a transparent PNG with different appearances on white and black backgrounds. This page does not add cover text or numbering; existing text in input images remains part of their pixels.

### Steps

1. Import images for the black-background appearance, then choose color or grayscale. By default, a text-free cover adapts to each image’s dimensions and palette; you can select a custom white-background cover.
2. Choose a processing method and read its changing explanation below. Both previews show the actual transparency composites.
3. Adjust color strength and output size, then refresh the preview. Original-color optimization allows a custom effective strength and a return to automatic adjustment.
4. Check both previews and export selected items or the queue. Use restoration here or in Dual image to extract the white- and black-background appearances.

### Options and usage details

- Balanced color uses the base color blend. Image priority favors the black-background image. Original-color optimization narrows the available gamut, preserves regional colors and softens fine color detail.
- Outline priority uses a uniform tint and exposes the fixed-color cover choices. Automatic color covers can follow the source palette or use a custom image. Grayscale mode uses no color cover.
- QR optimization accepts PNGs generated by this tool with their linkage information intact. White shows the cover, black reveals the QR, and export checks decoding.
- Requested and effective color strength differ because transparency has a limited color gamut. Raising effective strength retains more color but can expose more source information in the cover.

### Output, restoration and compatibility

Outputs use the Background reveal subfolder and must remain transparent PNGs. Extraction returns the adjusted appearances, not original file bytes. QQ, galleries and Explorer may use gray or checkerboard backgrounds or different compositing; white/black previews cannot guarantee zero interference in every viewer.

<a id="scramble"></a>
## 3. Scramble

Rearrange image pixels using a deterministic permutation, then reverse it to restore the image. Choose a mode that retains the original file or one that stores only processed pixels.

### Steps

1. Import images, select an item and compare the before/after previews.
2. Choose output quality. JPEG quality is adjustable; the preview shows the encoded output and its size.
3. Scramble the current item or the queue and save the generated files.
4. Import scrambled files to restore. Pixel restoration reverses the permutation; original-file restoration extracts retained original content from supported outputs.

### Options and usage details

- Original-file quality outputs a PNG with the original file attached, allowing byte-for-byte restoration at the cost of larger files.
- Lossless PNG (pixels only) retains rearranged pixels. Reversing restores pixels, but not the original encoding or metadata.
- Compressed JPEG reduces size but its lossy pixel changes cannot be undone by reversing the permutation. Restoration output quality can be set separately.
- Batch operations follow queue order. Pixel processing uses the first frame of animation; retained original files can contain the entire animation.

### Output, restoration and compatibility

Outputs use the Scramble subfolder. Resizing, screenshots, redaction and website re-encoding can break pixel restoration and remove the attached original. This feature obscures appearance; it is not password encryption.

<a id="disguise"></a>
## 4. File disguise

Combine a video or archive with a cover in a JPG/PNG-looking file, then extract the original. Videos are not transcoded and source archives are not recompressed.

### Steps

1. Import files into the video or archive section and reorder as needed. Choose a shared cover or a separate cover per item.
2. Set cover text, size, color and position. Multiple layers can be dragged, positioned and deleted independently; numbering follows queue order.
3. Select packaging and cover size, then export selected items or the queue.
4. Import generated files into restoration to detect packaging and extract videos or archives. To verify a transfer, compare the sent file with the received original file.

### Options and usage details

- Structured JPG packaging retains source information and verifies restoration. The copy/b concatenation mode joins bytes directly and does not retain the same source record.
- PNG with embedded ZIP stores the unchanged source in ZIP. The PNG IDAT ZIP variant is experimental and has more compatibility restrictions.
- For PNG ZIP outputs, copy the file, rename the copy to .zip and extract it; its entry remains the original MP4, RAR or other file. In-app restoration is simpler.
- Renaming a JPG alone does not guarantee video playback. Third-party extractors differ in support for concatenated files.
- Android retains the source cover aspect ratio without automatically cropping for packaging. The default longest edge is 1600 pixels; alternatives are 1200, 2048 or original size. The app may further scale proportionally to meet a 512 KiB cover budget. Without a selected cover, the automatic cover is 800×500. Inspect the exported cover.
- Android provides a manual QQ round-trip check: choose the packaged file before sending and the same packaged file saved after receipt, without extracting it first; compare actual bytes and SHA-256, confirm the client and transfer channel requested on the page, and export this report. It records this particular file comparison and user confirmation, without guaranteeing every QQ version, channel or file.

### Output, restoration and compatibility

Outputs use the File disguise subfolder. Transfer original files: image re-encoding by websites or chat apps can discard attached data. A successful comparison verifies that specific transfer, not every client. Packaging is not encryption.

<a id="gif"></a>
## 5. Make GIF

Combine still images into an animated GIF in queue order, with playback before export.

### Steps

1. Import at least two images and move or remove items to set playback order.
2. Set frame delay, longest-edge size and looping. Android allows 20–10000 milliseconds and a longest edge of 100–2048 pixels.
3. Click Play for frame-by-frame preview, inspect order, speed and padding, and use Stop as needed. Android stops playback when the page or window is hidden. Export after review.

### Options and usage details

- Shorter delays play faster. Larger dimensions usually increase file size and memory use.
- The first image sets the canvas aspect ratio. Others fit in the center; padding color is configured in Settings.
- GIF has a limited palette, so colors and gradients may change. Animated inputs contribute their first frame as a still image.
- Checked queue items determine the images to combine; without checks, all items are used. At least two participating images are required, in queue order.

### Output, restoration and compatibility

Outputs use the Make GIF subfolder. A GIF does not retain original source files; their encoding, metadata and detail lost through resizing cannot be recovered from it.

<a id="clean"></a>
## 6. Clean info

Remove generation prompts, workflows and selected camera metadata while retaining encoded image data, color information and animation structure where supported. Handles PNG/APNG, JPEG, WebP and GIF.

### Steps

1. Import files and select an item or process the queue.
2. If generation parameters matter, inspect and export them with Read tags first.
3. Run cleanup, save the new files and check their appearance and animation.

### Options and usage details

- Cleanup does not redraw the image or overwrite the source. Required information such as color descriptions and display orientation may remain.
- A format may support cleanup even if the current system cannot preview it.
- Keep an untouched copy of files with attached originals or QR linkage: cleanup can remove information needed for restoration or linked processing.

### Output, restoration and compatibility

Outputs use the Clean info subfolder and retain the source format where possible. Removed prompts and workflows cannot be reconstructed from the cleaned file; the original remains available.

<a id="redact"></a>
## 7. Redact

Select areas with a rectangle or brush and apply mosaic, blur or solid fill. Supports undo, redo and resetting to the original.

### Steps

1. Choose an image, select rectangle or brush and mark the area in the preview.
2. Choose the effect and adjust strength, brush diameter or fill color, then apply.
3. Undo returns to the previous edit; redo restores an undone edit. Clear selection removes only the uncommitted selection; reset restores the original image.
4. Review the result and save a PNG.

### Options and usage details

- On Windows, zoom with the wheel and pan with the middle button; brush size is adjustable. Touch drawing and zoom are available on Android, with a short painting pause after page scrolling to reduce accidental edits.
- History allows up to 50 states within a memory budget; large images may retain fewer. Editing after undo clears redo history.
- Solid fill replaces selected pixels; blur and mosaic retain some structure. Avoid redacting text or QR areas that need to remain readable.
- Android solid fill accepts presets or arbitrary RGBA / HEX colors. Confirm the color before applying; the source image and complete canvas remain available in the editing session.

### Output, restoration and compatibility

Outputs use the Redact subfolder as a static PNG of the full canvas. Animated inputs use the first frame. Sources are not overwritten; after closing the session, replaced original pixels cannot be restored from the redacted result.

<a id="tags"></a>
## 8. Read tags

Read existing Stable Diffusion WebUI / ComfyUI generation parameters stored in image metadata. This does not infer prompts from visual content.

### Steps

1. Import a PNG/APNG, JPEG or WebP that contains generation metadata.
2. Check separate WebUI and ComfyUI panels. Read both from one image in a single action or import different images into the panels. Android provides Read both panels and Copy all parameters.
3. Inspect positive/negative prompts and available model, seed, sampler, step and other parameters.
4. Copy individual fields, copy all or export TXT files.

### Options and usage details

- Unsupported custom workflow nodes may yield only partial parameters; results reflect what the file actually stores.
- Missing fields are not invented. Metadata cleanup, screenshots and website transcoding may remove generation information.
- Reading does not change the image. The two panels export separate TXT files and skip unloaded or empty panels.

### Output, restoration and compatibility

TXT outputs use the Read tags subfolder with unique names. This feature does not download models, generate images or recover deleted parameters.

<a id="qr"></a>
## 9. QR codes

Encode a website URL, text or vCard in a standard QR code, or extract QR content from a QQ, WeChat or other screenshot and fuse only that code into a base image. Standard QR scanners can read the result; the scanning app determines how to handle the content. Images, recognition and inference stay local with no uploads.

### Steps

1. Enter a complete URL or paste content, or click Import QR image (Import QR from screenshot on Android). Choose the required code when multiple are found. Imported content stays exact; names, avatars and screenshot interface elements are excluded.
2. Choose QR only, QR on an image, Pixel texture fusion, or Local AI embedded QR. Image modes require a base image and accept clothing, portraits, scenery and other subjects. On Windows, click Select image in the empty preview at the top of the page, or drag an image into that preview to use it as the base. On Android, tap the empty QR preview or Choose base image. Use the QR import button to extract content from QQ, WeChat or other QR screenshots.
3. Adjust size and horizontal/vertical placement, then choose whether dragging moves the QR or current text. Hold to drag on Android; text layers have independent settings, positions and deletion. Standard/pixel modes offer border adjustment; AI adds no white frame. AI on both platforms defaults to Manual placement (you choose the position), using an ordinary QR illustration for placement. You can also select Automatically place away from skin and hair. Select at most one placement option, or leave both off. Caption image and create prompt automatically always appears as a separate switch: turn it off to edit a manual prompt. Manual placement can also use automatic prompts. Successful generation shows the actual fused result; changing placement mode or position requires regeneration.
4. Local AI: choose an image and QR region, then click One-click AI generation. Windows first prepares private Python, dependencies, a base model, QR ControlNet, captioning and protection models. Android includes diffusion and recognition runtimes in the APK and prepares only missing models. Once prepared, captioning, prompt creation and inference run on the device without a connected computer or ComfyUI.
5. Both platforms adjust constraints and validate the decoded content in the full image and a thumbnail. Export becomes available after success; exhausted retries report failure rather than accepting an unreadable result. You can add text after the image has finished generating. Export checks again after text is added. Retest on the intended phone and sharing platform. On Windows, embedded QR results appear in the preview at the top of the page; click View top preview to return to it. Android results appear in the QR preview on this page; inspect them before export.
6. Import images or start camera scanning. Verify results, then copy or open the website. Transparent PNGs are automatically tried on white and black.

### Options and usage details

- Standard borders range from 0 to 12 modules, with at least 4 recommended. Pixel texture fusion keeps at least 4 modules and recognition cores. It is fast and needs no model or network, but does not synthesize new scenes. Both Windows and Android retain this strategy. Image modes retain the whole base image with proportional fitting; AI uses a model-compatible working canvas and merges the result into the whole base image; export does not crop the original into a QR square.
- Clear preview appears beside the QR import button. Windows clears the current base image, ordinary QR placement illustration and AI result; Android clears the current base image, QR preview and AI result. Tap the empty preview to choose a new base image, or drop an image on Windows. QR content, model choices and text settings are retained, and source image files are not deleted. Choose a base image and generate again.
- Download one Windows EXE to start; no separate Python, ComfyUI or WebUI installation is needed. First AI use needs connectivity to prepare the private environment and missing models. Choose the storage folder, then click Download models / Check and repair environment. The app remembers the folder and selected models, checks a bounded set of common ComfyUI/WebUI locations, Hugging Face caches and previous locations, and accepts an existing folder through Rescan / Verify models. The full actual path appears below each base model and ControlNet selector, with Open base model folder and Open ControlNet folder buttons. A single-file model opens its parent folder; reused models open their original location. Verified complete external packages are referenced directly; existing files on the same drive can be shared. A valid base model or ControlNet needs only its missing counterpart and small configuration files. An incomplete package that cannot be shared across drives stops with an explanation instead of downloading duplicate large weights. SD 1.5 is the default; matching SDXL models can be selected. Prepared inference works offline. Complete Diffusers directories or matching safetensors are supported; LoRA files are not base models.
- Windows provides Model and environment setup… in both the download area and beside generation. First use selects the default SD 1.5 setup: the DreamShaper 8 base model, QR Monster SD 1.5 QR ControlNet and CUDA / CPU compatible environment. Click One-click AI generation to prepare missing resources automatically, without choosing each component; Use default setup (recommended) restores this pairing. SDXL, custom local models and the separate CPU environment are labeled alternatives. They are prepared only when you select them; unselected alternatives are not downloaded. The SDXL alternative pairs SDXL Base 1.0 / QR Monster SDXL. Other local models require same-family QR ControlNet. Both families and their selections coexist; switching neither deletes nor copies weights. The compatible environment uses runtime under the model root; a separate CPU environment uses runtime/cpu. These environments install independently and share model files. CPU inference is slower. The actual selected environment path is shown and can be opened. Download / Repair prepares only the currently selected setup.
- Windows AI image recognition shares source-scene and local-material information; the final prompt uses the selected model profile. DreamShaper 8 and SDXL Base 1.0 have separate built-in profiles. Other local models initially use their family profile, so the same natural-language description is not guaranteed to suit every model. For model-specific tags or trigger words, select the model in Model and environment setup… and enable Customize automatic prompts and parameters for this model. Follow its model card when saving positive {prompt}, negative {negative} templates and generation settings; these placeholders retain source recognition. 0 uses automatic values. Overrides allow 8–60 steps, CFG 1–12, strength 0.15–0.85 and SD 1.5 CLIP skip 0–2; skin strength remains at most 0.28. Disable customization to restore the built-in profile. Switching loads that model’s saved settings and requires regeneration. Ordinary Canny / Depth ControlNet cannot replace QR models. SD2, FLUX, SD3, Refiner, Inpaint, Turbo, Lightning and LCM are outside this default workflow. Architecture checks or a successful scan do not prove visual quality across all images.
- On first use, automatic Windows AI storage prefers D:\AI-Models. If D is unavailable or lacks space, it tries data drives E, F and onward. A new automatic folder requires at least 20 GiB free; existing environments and manually chosen folders use an estimate of missing resources, runtime and headroom. An insufficient manually chosen location asks you to choose again rather than changing location. Without a suitable data drive, choose a folder manually; storage never automatically falls back to C. The app still checks the former default cache for reusable files.
- Windows offers Delete selected base model…, Delete selected ControlNet… and Clear all AI models and environment…. Confirmation lists actual folders, selected model paths, file count, size and external-reference count before deletion. Selected deletion handles only the corresponding app-managed or registered model. Clear all checks both the current storage folder and the former default cache when recognized as managed by this app. Other models, images and unmanaged files remain. External reused models lose only their references and selection; the original files remain and later automatic scanning may find them again. After cleanup, AI use requires preparing missing resources again.
- Windows model download sources can be Auto, Official or China mirror. Auto probes official and mirror connectivity and response time. A mainland China system region prefers a reachable mirror; other regions prefer the faster response, with fallback on failure. Downloads run in the background. Editing and generation on the QR page are locked during preparation, but you can switch to other tools and keep using them. Progress shows the verified file count / total, overall byte ratio, current filename, file size, transferred or verified bytes and speed. Stop cancels remaining preparation while retaining verified models. Ready appears only after every required file passes verification. Official pinned resources are checked against size and SHA-256; first and manual checks read entire files. Valid cached hashes are reused only while file identity and change information match. Correct models need no download or network probe. Custom safetensors receive structural, data-range and truncation checks plus recorded content fingerprints; without an author's checksum this does not certify author-level integrity. Model transfers resume partial files and replace only missing or damaged files; external references stay read-only. Initial SD 1.5 models use about 7 GiB and SDXL about 12 GiB; dependencies use additional space. Mirrors receive downloads, not images. Speed depends on the network and server load.
- The Windows private runtime does not change system PATH or require WebUI. Check and repair targets this app’s environment, not GPU hardware or driver upgrades. Without usable CUDA it falls back to slower CPU inference.
- Windows AI automatic policy considers both total and available VRAM. Devices around 8GB use model offloading; lower available memory uses sequential offloading. Sufficient high VRAM prioritizes quality. Insufficient system memory can still cause failure; completion, stop and error terminate inference and release resources.
- Windows AI retains source colors and limits newly darkened patches on light surfaces, allowing modules in the original blue, brown or other hues. Prompts consider both the whole image and local material details. SD 1.5 adds scanning gradient guidance or latent material refinement with compatible CUDA, at least 16 GiB total and 12 GiB available VRAM, unless the low-memory policy is selected; insufficient VRAM triggers a reduced retry.
- AI on both platforms offers Automatically place away from skin and hair and Manual placement (you choose the position), with the latter selected by default. These placement choices cannot be selected together: selecting one clears the other, but clearing the current option does not select the other. You can leave both off and generation still uses your manual position. Caption image and create prompt automatically controls prompting independently and always remains visible. Turn it off to edit a manual prompt, or turn it on for local captioning and automatic prompts. Placement choices do not hide this switch or the manual prompt input. Manual QR size defaults to 60%. Before generation, manual placement shows the base image with an ordinary QR illustration; drag the code or adjust size and horizontal/vertical percentages. The position frame is for editing only. Automatic avoidance shows only the base image before generation, without an ordinary QR illustration, and hides QR size, horizontal/vertical placement and QR dragging options so the app can choose a position. Successful generation shows the actual fused image, and export uses that finished result. The ordinary QR illustration cannot be exported as finished AI artwork. Manual placement uses the selected skin, clothing or background region within image-boundary and scanning-size constraints. Changes to placement mode or position require clicking AI generation again. If manual-region retries finish without passing scan validation, the result dialog offers Choose an automatic position for me, which switches to automatic mode and retries. Dismissing the dialog keeps your manual position; you can also choose automatic mode before generation. Automatic placement may adjust position, full output size or actual coverage and reports changes in progress. Skin regions must also pass protection and scan validation; success is not guaranteed on every skin area. Full module pixels remain intact while a local patch is redrawn; local texture changes, so every detail cannot be guaranteed. Android supports hold-to-drag QR placement; automatic avoidance lets the app choose the position.
- Automatic AI prompts on both platforms extract only style and material semantics from whole-image and local QR-region descriptions, guiding the original illustration or photo style and existing textile, building or plant textures. They ignore guessed actions, identities and props. Unknown or unreliable colors follow the source image; guesses about hair or hats do not change garment colors. Local material prompts avoid requesting new people, faces or body parts. Caption image and create prompt automatically is independent of QR placement: enable it for automatic prompts or turn it off to edit your prompt, including while manual placement is selected. Automatic captioning starts enabled. Placement changes do not turn it off, and automatic avoidance keeps its placement logic. Content, image, placement, prompt, model or generation-setting changes require regeneration; text edits do not reload models.
- Windows AI retains the original background by default; filling a blank background is opt-in. Local garment prompts follow the original colors, weave, design, silhouette and folds; blending uses source colors and smooth edges. Windows AI also tries to recover fine source weave on edited clothing, adopting it only after scan validation; failure falls back to validating the previous candidate. This weave restoration leaves candidate pixels outside clothing and on untouched clothing unchanged. Detected faces, glasses, hair and existing pixelated blocks become protected areas that retain source pixels. A manually selected body-skin region, such as an arm, neck or leg, can receive bounded tonal fusion in the original skin color, subject to protection and scan validation. Detection can miss or misclassify small people, illustrations, fine hair and irregular redaction, so inspect each preview.
- Fusion retains locator information required by general-purpose scanners. The compatible CUDA SD 1.5 material path with ample VRAM on Windows attempts local latent refinement, replacing continuous locator frames with a center and surrounding points in source colors. It then tries to restore source texture behind the points with only necessary tonal adjustments, accepting this variant after scan validation. Failed refinement retains the previous texture for continued validation. Three locator groups remain and recognition texture may be visible; there is no fully circular or locator-free mode. Natural appearance trades off with decoding reliability, and reference-level results are not guaranteed for every image. Android uses native CPU diffusion guided by source texture, circular centers and surrounding points. It adds no white or blurred backing plate and retains source pixels behind locator points. Automatic prompts for manual placement describe the selected local material, avoiding garment descriptions on background buildings or trees; automatic-avoidance prompt logic remains unchanged. Generated texture is mapped back to source colors. Failed scans trigger tonal calibration only at necessary points, and both the complete image and a thumbnail must pass before acceptance; continued failure is reported. Android does not include the Windows CUDA latent-refinement path, and circular locator points can remain visible. Windows CPU inference is slow and other GPUs have no dedicated inference backend.
- Background reveal linkage requires an exported PNG with its QR parameters intact and QR optimization selected. This process strengthens the black-background code again and may change natural AI texture. Retest the output; platform re-encoding can break linkage.
- Import supports decodable standard QR content, including recognizable friend codes, URLs, text and vCards. Exact content preserves spaces, line breaks and URL fragments. Some circular or dotted styles receive local recognition preprocessing without changing the source screenshot. Proprietary formats such as WeChat mini-program SunCodes cannot be converted to a generic QR; unreadable inputs report failure without guessing content. Non-HTTP(S) content can be copied but cannot be executed through Open website.
- Ordinary Android tools require Android 5 (API 21) or later; 32-bit systems use ordinary tools only. Local AI requires a 64-bit device running Android 7 or later. This update has no completed 32-bit device regression yet. The APK bundles arm64-v8a / x86_64 native diffusion and ONNX Runtime; no Python, connected computer or remote service is required. The default downloads complete DreamShaper 8 SD 1.5 weights, QR Monster SD 1.5 and local captioning, material segmentation and face-protection models. Prepared generation works offline without uploading images or prompts. Ordinary image tools require no AI models. CPU generation on phones takes time and substantial memory; insufficient memory reports a failure. Emulator tests do not establish speed or compatibility on every phone. Mobile compatibility uses actual RAM, currently available memory and a 64-bit OS, rather than release year alone. Automatic policy uses 384 pixels below 10 GiB total RAM (320 can be selected to reduce memory demand), otherwise 512 pixels, with up to four CPU threads by default; SD 1.5 needs at least 3 GiB available system memory and SDXL 9 GiB before inference. An active user-started task uses an Android foreground service and wake lock; system restrictions and interruption still apply.
- Android Models and runtime… selects default SD 1.5, experimental alternative SDXL, or imported same-family complete safetensors / GGUF base models and QR ControlNet. SDXL requires more memory and has no verified ARM64 phone quality or performance claim. Unselected setups are not downloaded. The mobile workflow cannot use a Diffusers shard directory, LoRA or mismatched architecture instead of a complete model. Prompt and parameter profiles are saved per model family and file, including custom templates retaining {prompt} and {negative}. Regenerate after switching models or settings. Incompatible models fail explicitly; visual quality is not guaranteed for every custom model. Generation settings include steps, CFG, denoising strength, ControlNet strength, seed, thread count and SD 1.5 CLIP skip. Enabling Fill blank background changes only detected blank edges and preserves protected subjects.
- Android stores models in AI-Models under available app storage, preferring external app storage with enough space. Models and runtime… lists available storage locations. Phones do not use the Windows D/E drive rule. Actual folders and selected model paths are shown; Open model folder displays an in-app list of real files, sizes and incomplete fragments, with path copying. Rescanning reuses verified files in managed locations; imports receive integrity and content-fingerprint checks. Changing setups does not recopy models. Uninstalling the APK may also remove app-specific model folders; manage backups according to Android storage rules.
- Android download sources also offer Auto, Official and Mainland China mirror. Connectivity probes and system region / carrier country information choose a reachable source with fallback. Background progress shows verified files / total, overall byte progress, current file size and live speed while other tools remain usable. Stop retains verified files and resumable fragments; Ready appears only after all required files pass size and SHA-256 verification. Registered unchanged files reuse verification caches; a manual rescan checks entire files. Deleting selected models or Clean all AI models and job cache requires confirmation and handles app-managed weights, fragments and job caches while retaining source images. The runtime bundled in the APK is removed by uninstalling the app.

### Output, restoration and compatibility

Outputs use the QR codes subfolder; AI exports PNG. The QR contains the selected URL, text or vCard rather than original image bytes. Website access still needs connectivity and destination permissions. Models retain their own licenses; the human parser follows NVIDIA SegFormer’s noncommercial license, so check separate authorization for commercial use. Windows AI checks the exact full content with two local decoders at original and thumbnail sizes. Validation establishes local decoding at the tested scales, not success with every phone, camera or recompressed version. Content is never opened automatically.

<a id="settings"></a>
## 10. Settings

Set interface language, padding and available default parameters, and view author, project and license information.

### Steps

1. Choose Chinese or English. Menus, controls and the guide follow the language; paths, filenames and source generation parameters are not translated.
2. Set canvas padding color for cover fitting, GIF and other operations that need padding.
3. Windows and Android offer defaults for dual-image numbering and export size plus GIF delay, size and looping. Saved Android defaults apply on the next launch; current queues are not permanent projects.
4. Choose output destinations in the tool pages. Windows remembers the output root; Android offers album or system-authorized custom-folder saving and system sharing. Open output location on Android opens the album; directly viewing custom folders depends on system directory-view support. Otherwise use a file manager.

### Options and usage details

- Switching tools retains queues and options within the current session, but queues are not permanent projects; save outputs before closing.
- Use an appropriate longest-edge limit for large images. Batch processing runs sequentially and can be canceled while retaining completed outputs.
- Author and project links open project information; local processing does not depend on them. The APK is for Android. iOS can scan website QR codes with its camera, but cannot install this APK.
- Queues allow an arbitrary checked subset, Select all and Clear selection. Batch or Export all processes checked items first, or the whole queue when none are checked; Export current uses the current row. Remove handles checked items first, otherwise the current row. Dual-image and disguise output uses each item’s actual cover pairing; checking does not change queue order.
- Android padding, text and solid redaction support color presets and manual RGBA / HEX entry. Review the color preview before confirmation. Colors affect fitting and actual output; solid redaction directly replaces selected pixels.

### Output, restoration and compatibility

Each tool uses its own subfolder and avoids overwriting sources. User guide is below Settings, works offline, and provides chapter jumps and return-to-contents buttons.

## Development and licenses

Windows uses .NET Framework / WinForms and Android uses a native Java interface. See [VALIDATION.md](docs/VALIDATION.md) for build and validation notes and [RELEASES.md](docs/RELEASES.md) for release notes.

Author: 光影若水. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Pixel texture fusion needs no models. Windows and Android local AI download open weights on demand and infer on the device. Native Android runtime licenses are in [native/THIRD_PARTY_NOTICES.md](android/native/THIRD_PARTY_NOTICES.md). Model licenses are separate from the app license.
