package com.guangyingruoshui.imagetools;
import android.graphics.Bitmap;

/** Cover-only pixel permutation; packaging and hidden content stay in their original formats. */
final class CoverScramble {
    static void apply(Bitmap image,Files.Job job)throws Exception{
        job.check();int w=image.getWidth(),h=image.getHeight();
        int[] pixels=new int[w*h];image.getPixels(pixels,0,w,0,0,w,h);
        int[] mixed=ScrambleEngine.transform(pixels,w,h,new ScrambleEngine.Options(),false,job);
        job.check();image.setPixels(mixed,0,w,0,0,w,h);
    }
}
