package com.guangyingruoshui.imagetools;

import android.graphics.Bitmap;
import android.graphics.Color;
import java.io.*;
import java.util.*;

final class BackgroundRevealTests {
    static void require(boolean ok,String message)throws IOException{if(!ok)throw new IOException("Background reveal: "+message);}
    static double gray(int v){return .299*((v>>>16)&255)+.587*((v>>>8)&255)+.114*(v&255);}


    static void sourceColorTests(File dir,Files.Job job)throws Exception{
        Bitmap source=Bitmap.createBitmap(180,120,Bitmap.Config.ARGB_8888);int[] palette={Color.RED,Color.GREEN,Color.BLUE,Color.YELLOW,Color.CYAN,Color.MAGENTA};for(int y=0;y<120;y++)for(int x=0;x<180;x++)source.setPixel(x,y,palette[x/30]);
        try{
            BackgroundRevealAuto.Result result=BackgroundRevealAuto.compose(null,source,true,85,job,0,true);Bitmap loaded=null,black=null,white=null;
            try{require(result.profile.sourceColor&&!result.profile.theme,"source colors became tinted");File file=new File(dir,"source-colors.png");BackgroundRevealAuto.encode(result.image,file,true,result.profile,job);loaded=Images.load(file,0,true);black=DualBlend.render(loaded,false,job);white=DualBlend.render(loaded,true,job);for(int y=0;y<120;y++)for(int x=0;x<180;x++){int q=white.getPixel(x,y);require(Math.max(Color.red(q),Math.max(Color.green(q),Color.blue(q)))-Math.min(Color.red(q),Math.min(Color.green(q),Color.blue(q)))<=Math.ceil(result.profile.chromaLimit)+1,"actual white PNG exceeds adaptive cap");}
                for(int i=0;i<6;i++){int q=black.getPixel(i*30+15,60),expected=palette[i];for(int shift:new int[]{16,8,0})for(int other:new int[]{16,8,0})if(((expected>>>shift)&255)>((expected>>>other)&255))require(((q>>>shift)&255)-((q>>>other)&255)>8,"regional hue lost");}
                require(DualBlend.restore(file,dir,0,job).size()==2,"source extraction");
            }finally{result.image.recycle();if(loaded!=null)loaded.recycle();if(black!=null)black.recycle();if(white!=null)white.recycle();}
            BackgroundRevealAuto.Result gray=BackgroundRevealAuto.compose(null,source,false,85,job,1,true);try{require(!gray.profile.sourceColor&&!gray.profile.theme,"gray entered color pipeline");for(int y=0;y<120;y++)for(int x=0;x<180;x++){int q=gray.image.getPixel(x,y);require(Color.red(q)==Color.green(q)&&Color.green(q)==Color.blue(q),"gray chroma");}}finally{gray.image.recycle();}
            Bitmap flat=Bitmap.createBitmap(72,48,Bitmap.Config.ARGB_8888),ramp=Bitmap.createBitmap(72,48,Bitmap.Config.ARGB_8888);flat.eraseColor(Color.GRAY);for(int y=0;y<48;y++)for(int x=0;x<72;x++){int v=x*255/71;ramp.setPixel(x,y,Color.rgb(v,v,v));}
            try{BackgroundRevealAuto.Result low=BackgroundRevealAuto.compose(null,flat,true,85,job,0,true),high=BackgroundRevealAuto.compose(null,ramp,true,85,job,0,true);try{require(high.profile.chromaLimit<low.profile.chromaLimit,"budget did not adapt to contrast");require(low.profile.chromaLimit>=16&&low.profile.chromaLimit<=48&&high.profile.chromaLimit>=16&&high.profile.chromaLimit<=48,"invalid budget: flat="+low.profile.chromaLimit+", ramp="+high.profile.chromaLimit);android.util.Log.i("AIPT-TEST","Source-color chroma budgets: flat="+low.profile.chromaLimit+", ramp="+high.profile.chromaLimit);}finally{low.image.recycle();high.image.recycle();}}finally{flat.recycle();ramp.recycle();}
        }finally{source.recycle();}
    }

    static void autoTests(File dir,Files.Job job)throws Exception{
        Bitmap cover=Bitmap.createBitmap(72,48,Bitmap.Config.ARGB_8888),dark=Bitmap.createBitmap(72,48,Bitmap.Config.ARGB_8888);cover.eraseColor(Color.rgb(255,165,0));
        try{
            for(int y=0;y<48;y++)for(int x=0;x<72;x++)dark.setPixel(x,y,Color.rgb(x*255/71,y*255/47,220));
            for(boolean color:new boolean[]{true,false}){
                BackgroundRevealAuto.Result result=BackgroundRevealAuto.compose(cover,dark,color,70,job);Bitmap decoded=null,white=null,black=null;
                try{
                    require(color?result.profile.theme:!result.profile.theme,"auto tradeoff");File out=new File(dir,color?"auto-color.png":"auto-gray.png");BackgroundRevealAuto.encode(result.image,out,color,result.profile,job);
                    decoded=Images.load(out,0,true);white=DualBlend.render(decoded,true,job);black=DualBlend.render(decoded,false,job);int first=white.getPixel(0,0);
                    for(int y=0;y<48;y++)for(int x=0;x<72;x++){int v=white.getPixel(x,y);require(Math.abs(Color.red(v)-Color.red(first))<=2&&Math.abs(Color.green(v)-Color.green(first))<=2&&Math.abs(Color.blue(v)-Color.blue(first))<=2,"auto flat cover leaks contours");}
                    int v=black.getPixel(36,24);require(color?Color.red(v)!=Color.blue(v):Color.red(v)==Color.green(v)&&Color.green(v)==Color.blue(v),"auto chroma");require(DualBlend.restore(out,dir,0,job).size()==2,"auto extraction");
                }finally{result.image.recycle();if(decoded!=null)decoded.recycle();if(white!=null)white.recycle();if(black!=null)black.recycle();}
            }
            dark.eraseColor(Color.BLUE);BackgroundRevealAuto.Result blue=BackgroundRevealAuto.compose(null,dark,true,70,job);dark.eraseColor(Color.RED);BackgroundRevealAuto.Result red=BackgroundRevealAuto.compose(null,dark,true,70,job);
            try{require(blue.profile.pattern&&red.profile.pattern&&blue.profile.tintR!=red.profile.tintR,"source palette/pattern");}finally{blue.image.recycle();red.image.recycle();}
            Files.Job cancel=new Files.Job();cancel.cancelled=true;boolean stopped=false;try{BackgroundRevealAuto.compose(null,dark,true,70,cancel);}catch(InterruptedIOException ex){stopped=true;}require(stopped,"auto cancellation");
        }finally{cover.recycle();dark.recycle();}
    }

    static void run(File dir,Files.Job job)throws Exception{
        sourceColorTests(dir,job);
        autoTests(dir,job);
        Random random=new Random(8173);
        for(int i=0;i<16000;i++){
            int w=0xff000000|random.nextInt(0x1000000),b=0xff000000|random.nextInt(0x1000000),v=BackgroundReveal.pixel(w,b,false,70),a=v>>>24,c=v&255;
            require(((v>>>16)&255)==c&&((v>>>8)&255)==c,"gray chroma");require(Math.abs(c*a/255.0-.5*gray(b))<=1.01,"black appearance");require(Math.abs(c*a/255.0+255-a-(127.5+.5*gray(w)))<=1.01,"white appearance");
            require(BackgroundReveal.pixel(w,b,true,0)==BackgroundReveal.pixel(w,b,false,100),"zero saturation");
        }
        for(int i=0;i<4000;i++){
            int cover=0xff000000|random.nextInt(0x1000000),other=0xff000000|random.nextInt(0x1000000),dark=0xff000000|random.nextInt(0x1000000),strength=i%101;
            int v=BackgroundReveal.pixel(cover,dark,true,strength,true),v2=BackgroundReveal.pixel(other,dark,true,strength,true);double l=gray(dark);
            for(int shift:new int[]{16,8,0}){double target=.5*(l+(((dark>>>shift)&255)-l)*strength/100.0),actual=((v>>>shift)&255)*(v>>>24)/255.0,otherActual=((v2>>>shift)&255)*(v2>>>24)/255.0;require(Math.abs(actual-target)<=.501,"priority black target");require(Math.abs(actual-otherActual)<=1.001,"priority cover dependence");}
        }
        Bitmap automatic=BackgroundReveal.automaticCover(80,120);try{require(automatic.getWidth()==80&&automatic.getHeight()==120,"automatic cover dimensions");}finally{automatic.recycle();}
        Bitmap w=Bitmap.createBitmap(32,24,Bitmap.Config.ARGB_8888),b=Bitmap.createBitmap(32,24,Bitmap.Config.ARGB_8888);b.eraseColor(Color.BLUE);
        StringBuilder fixtureReport=new StringBuilder("Targets: W=127.5+0.5*(luma(light)+(light-luma(light))*s), B=0.5*(luma(dark)+(dark-luma(dark))*s), s=color?0.7:0.\nComplementary yellow/blue targets can yield a neutral joint optimum.\n");
        try{for(int coverColor:new int[]{Color.YELLOW,Color.WHITE})for(boolean color:new boolean[]{false,true}){
            w.eraseColor(coverColor);
            Bitmap result=BackgroundReveal.compose(w,b,color,70,job);String coverName=coverColor==Color.YELLOW?"yellow":"white";File png=new File(dir,"background-"+coverName+(color?"-color.png":"-gray.png"));
            try{BackgroundReveal.encode(result,png,color,70,job);require(DualBlend.isLegacy(png)&&DualBlend.background(png)==0,"profile recognition");Bitmap loaded=Images.load(png,0,true),view=DualBlend.render(loaded,false,job);
                try{int p=view.getPixel(10,10),encoded=result.getPixel(10,10),decoded=loaded.getPixel(10,10);require(Color.alpha(decoded)>0&&Color.alpha(decoded)<255,"transparent roundtrip alpha");for(int shift:new int[]{16,8,0})require(Math.abs(((p>>>shift)&255)-BackgroundReveal.flatten(encoded,shift,0))<=1,"PNG black appearance changed");require(Color.alpha(encoded)==Color.alpha(decoded),"PNG alpha changed");if(!color)require(Color.red(p)==Color.green(p)&&Color.green(p)==Color.blue(p),"gray mode");else if(coverColor==Color.WHITE)require(Color.red(p)!=Color.blue(p),"white cover fails to retain blue source chroma");fixtureReport.append(coverName).append(" color=").append(color).append(" raw=").append(Integer.toHexString(encoded)).append(" decoded=").append(Integer.toHexString(decoded)).append(" black=").append(Integer.toHexString(p)).append(" whiteRGB=").append(BackgroundReveal.flatten(decoded,16,255)).append(',').append(BackgroundReveal.flatten(decoded,8,255)).append(',').append(BackgroundReveal.flatten(decoded,0,255)).append('\n');}
                finally{loaded.recycle();view.recycle();}
                require(DualBlend.restore(png,dir,0,job).size()==2,"extraction");
            }finally{result.recycle();}
        }
        try(OutputStream out=new FileOutputStream(new File(dir,"background-fixtures.txt"))){out.write(fixtureReport.toString().getBytes(Files.UTF8));}
        Files.Job cancel=new Files.Job();cancel.cancelled=true;boolean stopped=false;try{BackgroundReveal.compose(w,b,true,70,cancel);}catch(InterruptedIOException ex){stopped=true;}require(stopped,"cancellation");
        }finally{w.recycle();b.recycle();}
    }
}
