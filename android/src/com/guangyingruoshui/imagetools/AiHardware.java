package com.guangyingruoshui.imagetools;

import android.app.ActivityManager;
import android.content.Context;
import android.os.Build;
import java.io.IOException;

/** Device capabilities determine the default, rather than a phone's release year. */
final class AiHardware {
    static final long GIB=1024L*1024*1024;
    final long memory,available;final boolean lowMemory;final int processors;
    AiHardware(Context context){ActivityManager.MemoryInfo info=new ActivityManager.MemoryInfo();((ActivityManager)context.getSystemService(Context.ACTIVITY_SERVICE)).getMemoryInfo(info);memory=info.totalMem;available=info.availMem;lowMemory=info.lowMemory;processors=Runtime.getRuntime().availableProcessors();}
    int edge(String family){return memory<10*GIB?384:512;}
    int threads(){return Math.max(1,Math.min(4,processors));}
    void check(String family)throws IOException {
        if(Build.VERSION.SDK_INT<24||Build.SUPPORTED_64_BIT_ABIS.length==0)throw new IOException("离线 AI 需要 Android 7+ 和 64 位系统。 / Offline AI requires Android 7+ and a 64-bit OS.");
        long required="sdxl".equals(family)?9*GIB:3*GIB;
        if(lowMemory||available<required)throw new IOException("当前可用内存不足，尚未开始生图。请关闭其他应用，或选择 SD1.5 与低内存策略。 / Not enough available RAM; generation has not started. Close other apps or choose SD1.5 and the low-memory policy.\n"+AiModelStore.format(available)+" / "+AiModelStore.format(required));
    }
    String summary(boolean english){return (english?"Detected RAM: ":"检测到运行内存：")+AiModelStore.format(memory)+(english?"; available: ":"；当前可用：")+AiModelStore.format(available)+(english?". Automatic policy uses ":"。自动策略采用 ")+edge("sd15")+" px / "+threads()+(english?" CPU threads. Release year alone cannot guarantee offline AI compatibility.":" 个 CPU 线程。上市年份不能单独保证离线生图兼容性。");}
}
