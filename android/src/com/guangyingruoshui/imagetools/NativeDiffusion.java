package com.guangyingruoshui.imagetools;

import android.graphics.Bitmap;
import android.os.Build;
import java.io.File;
import java.util.concurrent.CancellationException;

/** Real on-device diffusion. Invoke generate on a worker thread and always close the session. */
final class NativeDiffusion implements AutoCloseable {
    interface Progress {
        void onProgress(int step, int total, float seconds);
        void onStatus(String message);
    }
    private static final String loadFailure;
    static {
        String failure = Build.VERSION.SDK_INT < 24 ? "Offline AI requires Android 7.0 (API 24) or later" : null;
        if (failure == null) {
            try { System.loadLibrary("aipt_diffusion"); }
            catch (UnsatisfiedLinkError | SecurityException error) { failure = error.toString(); }
        }
        loadFailure = failure;
    }
    private volatile long handle;
    static boolean isAvailable() { return loadFailure == null; }
    static String failureReason() { return loadFailure == null ? "" : loadFailure; }
    static String systemInfo() { return isAvailable() ? nativeSystemInfo() : failureReason(); }
    NativeDiffusion() {
        if (!isAvailable()) throw new IllegalStateException("Android offline AI backend unavailable: " + loadFailure);
        handle = nativeCreate();
        if (handle == 0) throw new IllegalStateException("Cannot create native diffusion session");
    }
    Bitmap generate(String model, String controlNet, String vae, String taesd,
                    Bitmap init, Bitmap control, Bitmap mask, String prompt, String negative,
                    int steps, float cfg, float denoise, float controlStrength, int threads, long seed,
                    String sampler, String scheduler, boolean vaeTiling, Progress progress) {
        return generate(model, controlNet, vae, taesd, init, control, mask, prompt, negative,
                        steps, cfg, denoise, controlStrength, threads, seed, sampler, scheduler, vaeTiling, 0, progress);
    }
    Bitmap generate(String model, String controlNet, String vae, String taesd,
                    Bitmap init, Bitmap control, Bitmap mask, String prompt, String negative,
                    int steps, float cfg, float denoise, float controlStrength, int threads, long seed,
                    String sampler, String scheduler, boolean vaeTiling, int clipSkip, Progress progress) {
        long current = handle;
        if (current == 0) throw new CancellationException("Diffusion session closed");
        checkModel(model, "base model"); checkModel(controlNet, "ControlNet");
        if (vae != null && !vae.isEmpty()) checkModel(vae, "VAE");
        if (taesd != null && !taesd.isEmpty()) checkModel(taesd, "TAESD");
        if (init == null || control == null || init.isRecycled() || control.isRecycled())
            throw new IllegalArgumentException("Initial image and QR control image are required");
        int width = init.getWidth(), height = init.getHeight();
        if (width < 64 || height < 64 || width > 1536 || height > 1536 || width % 64 != 0 || height % 64 != 0)
            throw new IllegalArgumentException("Inference canvas must be 64-1536 pixels and aligned to 64 pixels");
        if (control.getWidth() != width || control.getHeight() != height ||
            (mask != null && (mask.isRecycled() || mask.getWidth() != width || mask.getHeight() != height)))
            throw new IllegalArgumentException("Initial, control and mask image dimensions must match");
        if (steps < 1 || steps > 150 || !finite(cfg) || cfg < 0 || cfg > 30 ||
            !finite(denoise) || denoise <= 0 || denoise > 1 || !finite(controlStrength) ||
            controlStrength <= 0 || controlStrength > 5 || threads < 1 || threads > 32 || clipSkip < 0 || clipSkip > 2)
            throw new IllegalArgumentException("Invalid diffusion settings");
        Bitmap initial = null, hint = null, editMask = null;
        try {
            initial = argb(init); hint = argb(control); editMask = mask == null ? null : argb(mask);
            return nativeGenerate(current, model, controlNet, empty(vae), empty(taesd), initial, hint, editMask,
                                  empty(prompt), empty(negative), steps, cfg, denoise, controlStrength, threads, seed,
                                  empty(sampler), empty(scheduler), vaeTiling, clipSkip, progress);
        } finally {
            if (initial != null && initial != init) initial.recycle();
            if (hint != null && hint != control) hint.recycle();
            if (editMask != null && editMask != mask) editMask.recycle();
        }
    }
    void cancel() { long current = handle; if (current != 0) nativeCancel(current); }
    @Override public void close() {
        long current;
        synchronized (this) { current = handle; handle = 0; }
        if (current != 0) nativeRelease(current);
    }
    private static Bitmap argb(Bitmap bitmap) {
        if (bitmap.getConfig() == Bitmap.Config.ARGB_8888) return bitmap;
        Bitmap copy = bitmap.copy(Bitmap.Config.ARGB_8888, false);
        if (copy == null) throw new OutOfMemoryError("Cannot prepare inference bitmap");
        return copy;
    }
    private static boolean finite(float value) { return !Float.isNaN(value) && !Float.isInfinite(value); }
    private static String empty(String value) { return value == null ? "" : value; }
    private static void checkModel(String path, String label) {
        if (path == null || path.isEmpty() || !new File(path).isFile() || new File(path).length() == 0)
            throw new IllegalArgumentException("Missing " + label + " file");
    }
    private static native long nativeCreate();
    private static native void nativeCancel(long session);
    private static native void nativeRelease(long session);
    private static native String nativeSystemInfo();
    private static native Bitmap nativeGenerate(long session, String model, String controlNet, String vae, String taesd,
                                                Bitmap init, Bitmap control, Bitmap mask, String prompt, String negative,
                                                int steps, float cfg, float denoise, float controlStrength, int threads,
                                                long seed, String sampler, String scheduler, boolean vaeTiling, int clipSkip,
                                                Progress progress);
}
