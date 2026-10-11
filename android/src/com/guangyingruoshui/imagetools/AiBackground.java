package com.guangyingruoshui.imagetools;

import android.graphics.*;
import java.io.IOException;

/** Only near-white pixels connected to a canvas edge qualify as empty background. */
final class AiBackground {
    static Bitmap mask(Bitmap image,AiVision.MaterialMap materials,Files.Job job)throws IOException {
        int w=Math.max(1,Math.round(image.getWidth()*Math.min(1,192f/Math.max(image.getWidth(),image.getHeight())))),h=Math.max(1,Math.round(image.getHeight()*Math.min(1,192f/Math.max(image.getWidth(),image.getHeight()))));
        Bitmap small=Bitmap.createScaledBitmap(image,w,h,true);int[] pixels=new int[w*h],queue=new int[w*h];boolean[] found=new boolean[w*h];small.getPixels(pixels,0,w,0,0,w,h);if(small!=image)small.recycle();int end=0;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)if((x==0||y==0||x==w-1||y==h-1)&&blank(pixels[y*w+x])){int i=y*w+x;if(!found[i]){found[i]=true;queue[end++]=i;}}
        for(int at=0;at<end;at++){job.check();int i=queue[at],x=i%w,y=i/w;int[] adjacent={x>0?i-1:-1,x+1<w?i+1:-1,y>0?i-w:-1,y+1<h?i+w:-1};for(int next:adjacent)if(next>=0&&!found[next]&&blank(pixels[next])){found[next]=true;queue[end++]=next;}}
        if(end<pixels.length*.15)return null;
        Bitmap mask=Bitmap.createBitmap(image.getWidth(),image.getHeight(),Bitmap.Config.ARGB_8888);int[] row=new int[image.getWidth()];
        for(int y=0;y<image.getHeight();y++){job.check();for(int x=0;x<row.length;x++){int mx=Math.min(materials.width-1,x*materials.width/image.getWidth()),my=Math.min(materials.height-1,y*materials.height/image.getHeight());row[x]=found[Math.min(h-1,y*h/image.getHeight())*w+Math.min(w-1,x*w/image.getWidth())]&&!materials.isProtected(mx,my)?Color.WHITE:Color.BLACK;}mask.setPixels(row,0,row.length,0,y,row.length,1);}return mask;
    }
    static boolean blank(int color){int r=(color>>16)&255,g=(color>>8)&255,b=color&255;return Math.min(r,Math.min(g,b))>=242&&Math.max(r,Math.max(g,b))-Math.min(r,Math.min(g,b))<=12;}
    static void merge(Bitmap source,Bitmap generated,Bitmap mask,Files.Job job)throws IOException {Bitmap scaled=Bitmap.createScaledBitmap(generated,source.getWidth(),source.getHeight(),true);int w=source.getWidth();int[] original=new int[w],edited=new int[w],allowed=new int[w];try{for(int y=0;y<source.getHeight();y++){job.check();source.getPixels(original,0,w,0,y,w,1);scaled.getPixels(edited,0,w,0,y,w,1);mask.getPixels(allowed,0,w,0,y,w,1);for(int x=0;x<w;x++)if((allowed[x]&255)>127)original[x]=edited[x];source.setPixels(original,0,w,0,y,w,1);}}finally{if(scaled!=generated)scaled.recycle();}}
}
