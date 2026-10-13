package com.guangyingruoshui.imagetools;
strictfp final class ReferenceColors {
    static void adjust(int[] pixels,boolean enhance,Files.Job job)throws Exception {
        for(int i=0;i<pixels.length;i++){
            if((i&65535)==0)job.check();int p=pixels[i],r=(p>>>16)&255,g=(p>>>8)&255,b=p&255;
            double red=r/255.0,green=g/255.0,blue=b/255.0,max=Math.max(red,Math.max(green,blue)),min=Math.min(red,Math.min(green,blue)),l=(max+min)/2,h=0,s=0;
            if(max!=min){double d=max-min;s=l>.5?d/(2-max-min):d/(max+min);h=max==red?(green-blue)/d+(green<blue?6:0):max==green?(blue-red)/d+2:(red-green)/d+4;h/=6;h*=360;}
            s*=100;l*=100;if(h<75||h>165||s<=8||l<=10||l>=90)continue;
            double gf=enhance?1+Math.min(s,70)/100*.6:Math.max(.5,1-Math.min(s,80)/100*.5),rb=enhance?Math.max(.7,1-Math.min(s,70)/100*.3):Math.min(1.3,1+Math.min(s,80)/100*.3);
            r=Math.min(255,(int)Math.floor(r*rb+.5));g=Math.min(255,(int)Math.floor(g*gf+.5));b=Math.min(255,(int)Math.floor(b*rb+.5));pixels[i]=(p&0xff000000)|(r<<16)|(g<<8)|b;
        }
    }
}
