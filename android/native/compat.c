/* Only permits installation of ordinary image tools on 32-bit Android.
 * This library deliberately provides no diffusion or ONNX runtime symbols.
 */
__attribute__((visibility("default")))
const char* aipt_compat_description(void) {
    return "32-bit ordinary image tools; offline AI requires a 64-bit Android 7+ runtime";
}
