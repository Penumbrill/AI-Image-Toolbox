#include <jni.h>
#include <android/bitmap.h>
#include <android/log.h>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <map>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>
#include "stable-diffusion.h"

namespace {
struct Session {
    std::atomic<bool> cancelled{false};
    std::atomic<bool> running{false};
    std::mutex context_mutex;
    sd_ctx_t* context = nullptr;
    void cancel() {
        cancelled.store(true);
        std::lock_guard<std::mutex> lock(context_mutex);
        if (context) sd_cancel_generation(context, SD_CANCEL_ALL);
    }
};
std::mutex sessions_mutex, inference_mutex;
std::map<jlong, std::shared_ptr<Session>> sessions;
std::atomic<jlong> next_session{1};
struct Cancelled : std::runtime_error { Cancelled() : std::runtime_error("Offline AI generation cancelled") {} };
std::shared_ptr<Session> get_session(jlong id) {
    std::lock_guard<std::mutex> lock(sessions_mutex);
    auto found = sessions.find(id);
    if (found == sessions.end()) throw Cancelled();
    return found->second;
}
void check_cancel(const Session& session) { if (session.cancelled.load()) throw Cancelled(); }
std::string utf8(JNIEnv* env, jstring value) {
    if (!value) return {};
    const jchar* text = env->GetStringChars(value, nullptr);
    if (!text) throw std::bad_alloc();
    jsize length = env->GetStringLength(value);
    std::string result;
    result.reserve(length * 3);
    for (jsize i = 0; i < length; ++i) {
        uint32_t ch = text[i];
        if (ch >= 0xd800 && ch <= 0xdbff && i + 1 < length && text[i+1] >= 0xdc00 && text[i+1] <= 0xdfff)
            ch = 0x10000 + ((ch - 0xd800) << 10) + (text[++i] - 0xdc00);
        else if (ch >= 0xd800 && ch <= 0xdfff) ch = 0xfffd;
        if (ch < 0x80) result += char(ch);
        else if (ch < 0x800) { result += char(0xc0 | ch >> 6); result += char(0x80 | (ch & 63)); }
        else if (ch < 0x10000) { result += char(0xe0 | ch >> 12); result += char(0x80 | ((ch >> 6) & 63)); result += char(0x80 | (ch & 63)); }
        else { result += char(0xf0 | ch >> 18); result += char(0x80 | ((ch >> 12) & 63)); result += char(0x80 | ((ch >> 6) & 63)); result += char(0x80 | (ch & 63)); }
    }
    env->ReleaseStringChars(value, text);
    return result;
}
jstring from_utf8(JNIEnv* env, const std::string& value) {
    // Library logs are UTF-8; use Java's decoder rather than JNI's modified UTF-8.
    jbyteArray bytes = env->NewByteArray(static_cast<jsize>(value.size()));
    if (!bytes) return nullptr;
    env->SetByteArrayRegion(bytes, 0, static_cast<jsize>(value.size()), reinterpret_cast<const jbyte*>(value.data()));
    jclass cls = env->FindClass("java/lang/String");
    jmethodID ctor = cls ? env->GetMethodID(cls, "<init>", "([BLjava/lang/String;)V") : nullptr;
    jstring charset = env->NewStringUTF("UTF-8");
    jstring result = ctor ? static_cast<jstring>(env->NewObject(cls, ctor, bytes, charset)) : nullptr;
    env->DeleteLocalRef(bytes); env->DeleteLocalRef(charset); if (cls) env->DeleteLocalRef(cls);
    return result;
}
void throw_java(JNIEnv* env, const char* type, const std::string& message) {
    if (env->ExceptionCheck()) return;
    jclass cls = env->FindClass(type);
    if (cls) { env->ThrowNew(cls, message.c_str()); env->DeleteLocalRef(cls); }
}
struct Image {
    sd_image_t value{};
    std::vector<uint8_t> pixels;
    Image(JNIEnv* env, jobject bitmap, bool mask = false) {
        if (!bitmap) return;
        AndroidBitmapInfo info{};
        if (AndroidBitmap_getInfo(env, bitmap, &info) != ANDROID_BITMAP_RESULT_SUCCESS || info.format != ANDROID_BITMAP_FORMAT_RGBA_8888)
            throw std::invalid_argument("Inference bitmap must be ARGB_8888");
        if (info.width < 64 || info.height < 64 || info.width > 1536 || info.height > 1536 || info.width % 64 || info.height % 64)
            throw std::invalid_argument("Inference bitmap dimensions must be multiples of 64 (64-1536)");
        value.width = info.width; value.height = info.height; value.channel = mask ? 1 : 3;
        pixels.resize(static_cast<size_t>(info.width) * info.height * value.channel);
        void* raw = nullptr;
        if (AndroidBitmap_lockPixels(env, bitmap, &raw) != ANDROID_BITMAP_RESULT_SUCCESS)
            throw std::runtime_error("Cannot read inference bitmap pixels");
        for (uint32_t y = 0; y < info.height; ++y) {
            const auto* row = static_cast<uint8_t*>(raw) + y * info.stride;
            for (uint32_t x = 0; x < info.width; ++x) {
                size_t index = (static_cast<size_t>(y) * info.width + x) * value.channel;
                const uint8_t* pixel = row + x * 4;
                if (mask) pixels[index] = static_cast<uint8_t>((uint32_t(pixel[0]) + pixel[1] + pixel[2]) / 3);
                else for (uint32_t c = 0; c < 3; ++c) {
                    uint32_t color = pixel[c];
                    if ((info.flags & ANDROID_BITMAP_FLAGS_ALPHA_MASK) == ANDROID_BITMAP_FLAGS_ALPHA_UNPREMUL)
                        color = (color * pixel[3] + 127) / 255;
                    color += 255 - pixel[3];
                    pixels[index+c] = static_cast<uint8_t>(color > 255 ? 255 : color);
                }
            }
        }
        AndroidBitmap_unlockPixels(env, bitmap);
        value.data = pixels.data();
    }
};
struct Callback {
    JavaVM* vm = nullptr;
    jobject listener = nullptr;
    jmethodID progress = nullptr, status = nullptr;
    Session& session;
    std::mutex error_mutex;
    std::string error;
    std::atomic<bool> callback_failed{false};
    std::string last_error() { std::lock_guard<std::mutex> lock(error_mutex); return error; }
    Callback(JNIEnv* env, jobject target, Session& current) : session(current) {
        env->GetJavaVM(&vm);
        if (target) {
            listener = env->NewGlobalRef(target);
            jclass cls = env->GetObjectClass(target);
            progress = env->GetMethodID(cls, "onProgress", "(IIF)V");
            status = env->GetMethodID(cls, "onStatus", "(Ljava/lang/String;)V");
            env->DeleteLocalRef(cls);
            if (!listener || !progress || !status) throw std::runtime_error("Invalid generation progress callback");
        }
    }
    ~Callback() {
        sd_set_log_callback(nullptr, nullptr); sd_set_progress_callback(nullptr, nullptr);
        if (listener) { bool attached = false; JNIEnv* env = environment(attached); if (env) env->DeleteGlobalRef(listener); if (attached) vm->DetachCurrentThread(); }
    }
    JNIEnv* environment(bool& attached) {
        JNIEnv* env = nullptr;
        if (vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) == JNI_EDETACHED) {
            if (vm->AttachCurrentThread(&env, nullptr) != JNI_OK) return nullptr;
            attached = true;
        }
        return env;
    }
    void failed(JNIEnv* env) {
        if (env->ExceptionCheck()) { env->ExceptionClear(); callback_failed.store(true); session.cancel(); }
    }
    void send_status(const std::string& message) {
        if (!listener || callback_failed.load()) return;
        bool attached = false; JNIEnv* env = environment(attached);
        if (env) { jstring text = from_utf8(env, message); if (text) { env->CallVoidMethod(listener, status, text); env->DeleteLocalRef(text); } failed(env); }
        if (attached) vm->DetachCurrentThread();
    }
    void send_progress(int step, int total, float seconds) {
        if (!listener || callback_failed.load()) return;
        bool attached = false; JNIEnv* env = environment(attached);
        if (env) { env->CallVoidMethod(listener, progress, step, total, seconds); failed(env); }
        if (attached) vm->DetachCurrentThread();
    }
    static void log(enum sd_log_level_t level, const char* text, void* data) {
        auto& cb = *static_cast<Callback*>(data);
        if (level >= SD_LOG_WARN) __android_log_print(level == SD_LOG_ERROR ? ANDROID_LOG_ERROR : ANDROID_LOG_WARN, "AIPT-Diffusion", "%s", text);
        if (level == SD_LOG_ERROR) { std::lock_guard<std::mutex> lock(cb.error_mutex); cb.error = text ? text : "Native inference failed"; }
        if (level >= SD_LOG_INFO) cb.send_status(text ? text : "");
    }
    static void tick(int step, int total, float seconds, void* data) {
        auto& callback = *static_cast<Callback*>(data);
        // Upstream resets its cancellation flag at the start of generate_image.
        // Keep a stop request made at that boundary effective at the first tick.
        if (callback.session.cancelled.load()) callback.session.cancel();
        callback.send_progress(step, total, seconds);
    }
};
struct Context {
    Session& session;
    sd_ctx_t* value;
    Context(Session& current, sd_ctx_t* context) : session(current), value(context) {
        std::lock_guard<std::mutex> lock(session.context_mutex);
        session.context = value;
        if (value && session.cancelled.load()) sd_cancel_generation(value, SD_CANCEL_ALL);
    }
    ~Context() {
        { std::lock_guard<std::mutex> lock(session.context_mutex); session.context = nullptr; }
        if (value) free_sd_ctx(value);
    }
};
struct Results {
    sd_image_t* images = nullptr;
    int count = 0;
    ~Results() { if (images) free_sd_images(images, count); }
};
jobject bitmap_result(JNIEnv* env, const sd_image_t& image) {
    if (!image.data || image.channel < 3) throw std::runtime_error("Native diffusion returned invalid image data");
    jclass config = env->FindClass("android/graphics/Bitmap$Config");
    jfieldID rgba_id = env->GetStaticFieldID(config, "ARGB_8888", "Landroid/graphics/Bitmap$Config;");
    jobject rgba = env->GetStaticObjectField(config, rgba_id);
    jclass bitmap = env->FindClass("android/graphics/Bitmap");
    jmethodID create = env->GetStaticMethodID(bitmap, "createBitmap", "(IILandroid/graphics/Bitmap$Config;)Landroid/graphics/Bitmap;");
    jobject result = env->CallStaticObjectMethod(bitmap, create, jint(image.width), jint(image.height), rgba);
    env->DeleteLocalRef(config); env->DeleteLocalRef(rgba); env->DeleteLocalRef(bitmap);
    if (!result || env->ExceptionCheck()) return nullptr;
    AndroidBitmapInfo info{}; void* raw = nullptr;
    if (AndroidBitmap_getInfo(env, result, &info) != 0 || AndroidBitmap_lockPixels(env, result, &raw) != 0)
        throw std::runtime_error("Cannot write generated bitmap");
    for (uint32_t y = 0; y < image.height; ++y) for (uint32_t x = 0; x < image.width; ++x) {
        uint8_t* dest = static_cast<uint8_t*>(raw) + y * info.stride + x * 4;
        const uint8_t* src = image.data + (static_cast<size_t>(y) * image.width + x) * image.channel;
        dest[0] = src[0]; dest[1] = src[1]; dest[2] = src[2]; dest[3] = 255;
    }
    AndroidBitmap_unlockPixels(env, result);
    return result;
}
}

extern "C" JNIEXPORT jlong JNICALL Java_com_guangyingruoshui_imagetools_NativeDiffusion_nativeCreate(JNIEnv* env, jclass) {
    try { auto session = std::make_shared<Session>(); jlong id = next_session.fetch_add(1); std::lock_guard<std::mutex> lock(sessions_mutex); sessions.emplace(id, session); return id; }
    catch (const std::bad_alloc&) { throw_java(env, "java/lang/OutOfMemoryError", "Cannot allocate diffusion session"); return 0; }
}
extern "C" JNIEXPORT void JNICALL Java_com_guangyingruoshui_imagetools_NativeDiffusion_nativeCancel(JNIEnv*, jclass, jlong id) {
    try { get_session(id)->cancel(); } catch (const Cancelled&) {}
}
extern "C" JNIEXPORT void JNICALL Java_com_guangyingruoshui_imagetools_NativeDiffusion_nativeRelease(JNIEnv*, jclass, jlong id) {
    std::shared_ptr<Session> session;
    { std::lock_guard<std::mutex> lock(sessions_mutex); auto found = sessions.find(id); if (found == sessions.end()) return; session = found->second; sessions.erase(found); }
    session->cancel();
}
extern "C" JNIEXPORT jstring JNICALL Java_com_guangyingruoshui_imagetools_NativeDiffusion_nativeSystemInfo(JNIEnv* env, jclass) {
    return from_utf8(env, std::string("stable-diffusion.cpp ") + sd_commit() + "\n" + sd_get_system_info());
}
extern "C" JNIEXPORT jobject JNICALL Java_com_guangyingruoshui_imagetools_NativeDiffusion_nativeGenerate(
    JNIEnv* env, jclass, jlong id, jstring model, jstring control_net, jstring vae, jstring taesd,
    jobject initial, jobject control, jobject mask, jstring prompt, jstring negative, jint steps, jfloat cfg,
    jfloat denoise, jfloat control_strength, jint threads, jlong seed, jstring sampler, jstring scheduler,
    jboolean vae_tiling, jint clip_skip, jobject listener) {
    try {
        auto session = get_session(id);
        if (session->running.exchange(true)) throw std::runtime_error("This diffusion session is already generating");
        struct Running { Session& session; ~Running() { session.running.store(false); } } running{*session};
        check_cancel(*session);
        std::unique_lock<std::mutex> serial(inference_mutex);
        check_cancel(*session);
        auto model_path = utf8(env, model), control_path = utf8(env, control_net), vae_path = utf8(env, vae), tae_path = utf8(env, taesd);
        auto positive = utf8(env, prompt), negative_prompt = utf8(env, negative), method = utf8(env, sampler), schedule = utf8(env, scheduler);
        if (model_path.empty() || control_path.empty()) throw std::invalid_argument("Base model and ControlNet files are required");
        if (steps < 1 || steps > 150 || !std::isfinite(cfg) || cfg < 0 || cfg > 30 || !std::isfinite(denoise) || denoise <= 0 || denoise > 1 ||
            !std::isfinite(control_strength) || control_strength <= 0 || control_strength > 5 || threads < 1 || threads > 32 || clip_skip < 0 || clip_skip > 2)
            throw std::invalid_argument("Invalid native inference settings");
        Image init(env, initial), hint(env, control), edit_mask(env, mask, true);
        if (!init.value.data || !hint.value.data || init.value.width != hint.value.width || init.value.height != hint.value.height ||
            (edit_mask.value.data && (init.value.width != edit_mask.value.width || init.value.height != edit_mask.value.height)))
            throw std::invalid_argument("Initial, control and mask bitmap dimensions must match");
        Callback callback(env, listener, *session);
        sd_set_log_callback(Callback::log, &callback); sd_set_progress_callback(Callback::tick, &callback);
        callback.send_status("Loading on-device base model and ControlNet");
        check_cancel(*session);
        sd_ctx_params_t context_params; sd_ctx_params_init(&context_params);
        context_params.model_path = model_path.c_str(); context_params.control_net_path = control_path.c_str();
        context_params.vae_path = vae_path.c_str(); context_params.taesd_path = tae_path.c_str();
        context_params.n_threads = threads; context_params.enable_mmap = true;
        context_params.backend = "CPU"; context_params.params_backend = "CPU";
        context_params.rng_type = CPU_RNG; context_params.sampler_rng_type = CPU_RNG;
        context_params.flash_attn = true; context_params.diffusion_flash_attn = true;
        context_params.conditioning_cache_size = 0;
        Context context(*session, new_sd_ctx(&context_params));
        check_cancel(*session);
        if (!context.value) { auto error = callback.last_error(); throw std::runtime_error(error.empty() ? "Failed to load model or ControlNet; verify format, pairing and available memory" : error); }
        std::string family = sd_get_model_version_name(context.value);
        if (family.find("SD 1.x") != 0 && family.find("SDXL") != 0)
            throw std::invalid_argument("Offline QR ControlNet supports SD1.x and experimental SDXL checkpoints only");
        if (family.find("SDXL") == 0 && clip_skip != 0)
            throw std::invalid_argument("CLIP skip is disabled for SDXL QR generation");
        if (!sd_ctx_supports_image_generation(context.value) || !sd_ctx_has_control_net(context.value))
            throw std::runtime_error("The loaded model has no compatible image-generation ControlNet");
        sd_img_gen_params_t params; sd_img_gen_params_init(&params);
        params.prompt = positive.c_str(); params.negative_prompt = negative_prompt.c_str();
        params.clip_skip = clip_skip == 0 ? -1 : clip_skip;
        params.init_image = init.value; params.mask_image = edit_mask.value; params.control_image = hint.value;
        params.width = init.value.width; params.height = init.value.height;
        params.strength = denoise; params.control_strength = control_strength; params.seed = seed; params.batch_count = 1;
        params.sample_params.sample_steps = steps; params.sample_params.guidance.txt_cfg = cfg;
        params.sample_params.sample_method = method.empty() ? EULER_A_SAMPLE_METHOD : str_to_sample_method(method.c_str());
        if (params.sample_params.sample_method >= SAMPLE_METHOD_COUNT) throw std::invalid_argument("Unknown diffusion sampler");
        params.sample_params.scheduler = schedule.empty() ? DISCRETE_SCHEDULER : str_to_scheduler(schedule.c_str());
        if (params.sample_params.scheduler >= SCHEDULER_COUNT) throw std::invalid_argument("Unknown diffusion scheduler");
        params.vae_tiling_params.enabled = vae_tiling;
        params.vae_tiling_params.tile_size_w = 256; params.vae_tiling_params.tile_size_h = 256;
        params.vae_tiling_params.target_overlap = 0.25f;
        callback.send_status("Generating offline with " + family);
        check_cancel(*session);
        Results results;
        bool ok = generate_image(context.value, &params, &results.images, &results.count);
        if (callback.callback_failed.load()) throw std::runtime_error("Generation progress callback failed");
        check_cancel(*session);
        auto error = callback.last_error();
        if (!ok || results.count != 1 || !results.images || !error.empty())
            throw std::runtime_error(error.empty() ? "On-device diffusion failed; no generated image was returned" : error);
        if (results.images[0].width != init.value.width || results.images[0].height != init.value.height)
            throw std::runtime_error("Generated image dimensions differ from inference canvas");
        return bitmap_result(env, results.images[0]);
    } catch (const Cancelled& error) { throw_java(env, "java/util/concurrent/CancellationException", error.what()); }
      catch (const std::bad_alloc&) { throw_java(env, "java/lang/OutOfMemoryError", "Insufficient memory for offline model and ControlNet; use a smaller inference canvas or quantized SD1.5 checkpoint"); }
      catch (const std::invalid_argument& error) { throw_java(env, "java/lang/IllegalArgumentException", error.what()); }
      catch (const std::exception& error) { throw_java(env, "java/lang/IllegalStateException", error.what()); }
    return nullptr;
}
