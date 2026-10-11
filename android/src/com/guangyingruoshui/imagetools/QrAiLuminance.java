package com.guangyingruoshui.imagetools;

import android.graphics.*;

/** Source-colour compositing and decoder-checked, sparse tonal corrections after real diffusion. */
final class QrAiLuminance {
    static final double[] POINT_X={3,2.1213203436,0,-2.1213203436,-3,-2.1213203436,0,2.1213203436},POINT_Y={0,2.1213203436,3,2.1213203436,0,-2.1213203436,-3,-2.1213203436};
    static double luminance(int color){return .299*((color>>16)&255)+.587*((color>>8)&255)+.114*(color&255);}

    /** Restore source chroma and backing texture; no filled or blurred QR plate. */
    static void prepare(Bitmap image,Bitmap original,QrService.Region region,Files.Job job)throws Exception {
        int modules=QrService.matrix(region.url,1,region.margin,region.raw).getWidth(),scale=region.size/modules,data=modules-region.margin*2;
        int[] edited=new int[region.size],source=new int[region.size];
        for(int y=0;y<region.size;y++){
            job.check();image.getPixels(edited,0,edited.length,region.x,region.y+y,edited.length,1);original.getPixels(source,0,source.length,region.x,region.y+y,source.length,1);
            for(int x=0;x<edited.length;x++){
                double mx=(x+.5)/scale-region.margin,my=(y+.5)/scale-region.margin;
                boolean quiet=mx<0||my<0||mx>=data||my>=data;int locator=locatorSample(mx,my,data);
                if(quiet||locatorArea(mx,my,data)&&locator!=0){edited[x]=source[x];continue;}
                double distance=Math.min(Math.min(mx,my),Math.min(data-mx,data-my));
                double blend=.6*Math.min(1,Math.max(0,distance/2.5));if(locator>=0)blend=.5;
                double old=luminance(source[x]),value=old+Math.max(-72,Math.min(72,luminance(edited[x])-old))*blend;
                edited[x]=palette(source[x],Math.max(Math.min(old,97),value));
            }
            image.setPixels(edited,0,edited.length,region.x,region.y+y,edited.length,1);
        }
    }

    static boolean refine(Bitmap image,Bitmap original,QrService.Region region,AiVision.MaterialMap materials,boolean automatic,Files.Job job)throws Exception {
        int modules=QrService.matrix(region.url,1,region.margin,region.raw).getWidth(),scale=region.size/modules;
        Bitmap code=QrService.code(region.url,region.size,region.margin,region.raw),baseline=image.copy(Bitmap.Config.ARGB_8888,false);
        try {
            Paint reset=new Paint();reset.setXfermode(new PorterDuffXfermode(PorterDuff.Mode.SRC));
            for(int pass=0;pass<5;pass++){
                new Canvas(image).drawBitmap(baseline,0,0,reset);
                for(int cycle=0;cycle<2;cycle++){
                    long[] integral=integral(image,region,job);adjust(image,original,code,region,modules,scale,integral,pass,job);
                    AndroidAiController.restoreProtected(image,original,materials,region,automatic,job);
                    if(QrService.matches(image,region.url,job)&&QrArt.reducedMatches(image,region.url,job)){
                        android.util.Log.i("AIPT-AI","circular_tone_pass="+pass+" cycle="+cycle);return true;
                    }
                }
            }
            return false;
        }finally{code.recycle();baseline.recycle();}
    }

    static long[] integral(Bitmap image,QrService.Region region,Files.Job job)throws Exception {
        int stride=region.size+1;long[] sum=new long[stride*stride];int[] row=new int[region.size];
        for(int y=0;y<region.size;y++){job.check();image.getPixels(row,0,row.length,region.x,region.y+y,row.length,1);long running=0;for(int x=0;x<row.length;x++){running+=Math.round(luminance(row[x]));sum[(y+1)*stride+x+1]=sum[y*stride+x+1]+running;}}
        return sum;
    }
    static double threshold(long[] sum,int size,int x,int y,int radius){
        int stride=size+1,left=Math.max(0,x-radius),right=Math.min(size,x+radius+1),top=Math.max(0,y-radius),bottom=Math.min(size,y+radius+1);
        double value=(sum[bottom*stride+right]-sum[top*stride+right]-sum[bottom*stride+left]+sum[top*stride+left])/(double)((right-left)*(bottom-top));
        return Math.max(96,Math.min(211,value));
    }

    static void adjust(Bitmap output,Bitmap original,Bitmap code,QrService.Region region,int modules,int scale,long[] sum,int pass,Files.Job job)throws Exception {
        int[] row=new int[region.size],hint=new int[region.size],source=new int[region.size];int data=modules-region.margin*2;
        double radius=.25+pass*.045;int margin=14+pass*4;
        for(int y=0;y<region.size;y++){
            job.check();output.getPixels(row,0,row.length,region.x,region.y+y,row.length,1);original.getPixels(source,0,row.length,region.x,region.y+y,row.length,1);code.getPixels(hint,0,hint.length,0,y,hint.length,1);
            for(int x=0;x<row.length;x++){
                double mx=(x+.5)/scale-region.margin,my=(y+.5)/scale-region.margin;
                int locator=locatorSample(mx,my,data);boolean quiet=mx<0||my<0||mx>=data||my>=data;
                if(locatorArea(mx,my,data)&&locator<0||quiet&&locator<0){row[x]=source[x];continue;}
                boolean ink;double coverage=1;int difference=margin;
                if(locator>=0){ink=locator==0;difference=ink?28+pass*4:12+pass*3;}
                else {
                    double dx=(x%scale+.5)/scale-.5,dy=(y%scale+.5)/scale-.5,distance=Math.hypot(dx,dy);
                    if(distance>=radius)continue;coverage=Math.min(1,(radius-distance)/.07);ink=(hint[x]&0xffffff)==0;
                    if(QrArt.function((int)mx,(int)my,data))difference+=8;
                }
                double field=threshold(sum,region.size,x,y,Math.max(16,scale*2)),old=luminance(row[x]);
                double target=ink?Math.max(64,Math.min(155,field-difference)):Math.min(232,Math.max(146,field+difference));
                // Stable locator sampling permits lighter, smaller corrections over the data texture.
                if(locator>=0)target=ink?Math.min(target,108):Math.max(target,182);
                if(ink?old<=target:old>=target)continue;row[x]=palette(source[x],old+(target-old)*coverage);
            }
            output.setPixels(row,0,row.length,region.x,region.y+y,row.length,1);
        }
    }

    static boolean locatorArea(double x,double y,int data){return Math.abs(x-3.5)<4.5&&Math.abs(y-3.5)<4.5||Math.abs(x-(data-3.5))<4.5&&Math.abs(y-3.5)<4.5||Math.abs(x-3.5)<4.5&&Math.abs(y-(data-3.5))<4.5;}
    static int locatorSample(double x,double y,int data){
        if(!locatorArea(x,y,data))return -1;
        int value=pointSample(x-3.5,y-3.5);if(value>=0)return value;
        value=pointSample(x-(data-3.5),y-3.5);if(value>=0)return value;
        return pointSample(x-3.5,y-(data-3.5));
    }
    static int pointSample(double dx,double dy){double square=dx*dx+dy*dy;if(square<2.25)return 0;for(int point=0;point<8;point++){double x=dx-POINT_X[point],y=dy-POINT_Y[point];if(x*x+y*y<.3025)return 0;}boolean axis=Math.abs(dx)<.6||Math.abs(dy)<.6,diagonal=Math.abs(Math.abs(dx)-Math.abs(dy))<.85;return square<20.25&&(axis||diagonal)?1:-1;}

    /** Same source-chroma/gamut logic as desktop matching_palette. */
    static int palette(int source,double value){
        double y=Math.max(0,Math.min(255,value)),old=luminance(source),r=((source>>16)&255)-old,g=((source>>8)&255)-old,b=(source&255)-old,saturation=1;
        saturation=Math.min(saturation,gamut(y,r));saturation=Math.min(saturation,gamut(y,g));saturation=Math.min(saturation,gamut(y,b));
        return 0xff000000|((int)Math.round(y+r*saturation)<<16)|((int)Math.round(y+g*saturation)<<8)|(int)Math.round(y+b*saturation);
    }
    static double gamut(double y,double offset){return offset>0?(255-y)/offset:offset<0?y/-offset:1;}
}
