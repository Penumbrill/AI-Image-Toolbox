package com.guangyingruoshui.imagetools;

import android.graphics.*;
import com.google.zxing.common.BitMatrix;

/** Material control image with the same circular point geometry used by final calibration. */
final class QrAiGeometry {
    static Bitmap guide(Bitmap source,QrService.Region region,int edge,int left,int top,int side,Files.Job job)throws Exception {
        BitMatrix bits=QrService.matrix(region.url,1,region.margin,region.raw);
        int modules=bits.getWidth(),data=modules-region.margin*2,scale=region.size/modules;
        Bitmap frame=Bitmap.createBitmap(edge,edge,Bitmap.Config.ARGB_8888);
        new Canvas(frame).drawBitmap(source,new Rect(left,top,left+side,top+side),new Rect(0,0,edge,edge),new Paint(3));
        int[] row=new int[edge];
        try {
            for(int y=0;y<edge;y++){
                job.check();frame.getPixels(row,0,edge,0,y,edge,1);
                double gy=(top+(y+.5)*side/edge-region.y)/scale,my=gy-region.margin;
                for(int x=0;x<edge;x++){
                    double gx=(left+(x+.5)*side/edge-region.x)/scale,mx=gx-region.margin;
                    double gray=QrAiLuminance.luminance(row[x]);
                    if(gx>=0&&gy>=0&&gx<modules&&gy<modules){
                        int locator=QrAiLuminance.locatorSample(mx,my,data);
                        boolean quiet=mx<0||my<0||mx>=data||my>=data;
                        if(locator>=0)gray=locator==0?Math.min(gray,78):Math.max(gray,191);
                        else if(!quiet&&!QrAiLuminance.locatorArea(mx,my,data)){
                            double dx=gx-Math.floor(gx)-.5,dy=gy-Math.floor(gy)-.5,distance=Math.hypot(dx,dy);
                            double influence=Math.max(0,Math.min(1,(.38-distance)/.09));
                            boolean ink=bits.get((int)gx,(int)gy);
                            double target=ink?Math.min(gray,90):Math.max(gray,200);
                            gray+=(target-gray)*influence;
                        }
                    }
                    int value=(int)Math.round(gray);row[x]=0xff000000|value<<16|value<<8|value;
                }
                frame.setPixels(row,0,edge,0,y,edge,1);
            }
            return frame;
        }catch(Throwable error){frame.recycle();throw error;}
    }
}
