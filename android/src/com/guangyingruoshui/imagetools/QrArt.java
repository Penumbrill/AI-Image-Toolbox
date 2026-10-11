package com.guangyingruoshui.imagetools;

import android.graphics.*;
import java.io.IOException;
import java.io.ByteArrayOutputStream;

final class QrArt {
    static QrService.Region compose(Bitmap result,String url,int percent,int x,int y,int margin,Files.Job job)throws Exception{return compose(result,url,percent,x,y,margin,job,false);}
    static QrService.Region compose(Bitmap result,String url,int percent,int x,int y,int margin,Files.Job job,boolean raw)throws Exception{
        margin=Math.max(4,margin);Bitmap source=result.copy(Bitmap.Config.ARGB_8888,true);
        try{
            QrService.Region region=QrService.compose(result,url,percent,x,y,false,margin,raw);int modules=QrService.matrix(url,1,margin,raw).getWidth(),scale=region.size/modules;
            Bitmap code=QrService.code(url,region.size,margin,raw);
            try{
                for(int pass=0;pass<3;pass++){
                    job.check();new Canvas(result).drawBitmap(source,0,0,new Paint());blend(result,code,region,modules,scale,pass,job);
                    if(QrService.matches(result,region.url,job)&&reducedMatches(result,region.url,job)&&compressedMatches(result,region.url,job))return region;
                }
            }finally{code.recycle();}
            throw new IOException("艺术二维码自检失败，请增大二维码或改用图片附上二维码 / Artistic QR validation failed; enlarge the QR or use a standard overlay");
        }finally{source.recycle();}
    }
    static boolean reducedMatches(Bitmap image,String expected,Files.Job job)throws Exception{float ratio=Math.min(1,900f/Math.max(image.getWidth(),image.getHeight()));Bitmap small=Images.fit(image,Math.max(1,(int)(image.getWidth()*ratio)),Math.max(1,(int)(image.getHeight()*ratio)),Color.WHITE);try{return QrService.matches(small,expected,job);}finally{small.recycle();}}
    static boolean compressedMatches(Bitmap image,String expected,Files.Job job)throws Exception{ByteArrayOutputStream bytes=new ByteArrayOutputStream();if(!image.compress(Bitmap.CompressFormat.JPEG,90,bytes))throw new IOException("JPEG validation failed");byte[] data=bytes.toByteArray();Bitmap decoded=BitmapFactory.decodeByteArray(data,0,data.length);if(decoded==null)throw new IOException("JPEG validation failed");try{return QrService.matches(decoded,expected,job);}finally{decoded.recycle();}}
    static boolean function(int x,int y,int n){return (x<9&&y<9)||(x>=n-8&&y<9)||(x<9&&y>=n-8)||x==6||y==6;}
    static void blend(Bitmap output,Bitmap code,QrService.Region r,int modules,int scale,int pass,Files.Job job)throws Exception{
        int[] pixels=new int[r.size],mask=new int[r.size];double core=pass==0?.48:pass==1?.72:1;
        for(int py=0;py<r.size;py++){
            job.check();output.getPixels(pixels,0,r.size,r.x,r.y+py,r.size,1);code.getPixels(mask,0,r.size,0,py,r.size,1);
            for(int px=0;px<r.size;px++){
                int mx=px/scale-r.margin,my=py/scale-r.margin,n=modules-2*r.margin;boolean quiet=mx<0||my<0||mx>=n||my>=n,solid=quiet||function(mx,my,n);
                double dx=Math.abs((px%scale+.5)/scale-.5),dy=Math.abs((py%scale+.5)/scale-.5);boolean center=dx<=core/2&&dy<=core/2;
                if(!solid&&!center)continue;boolean ink=(mask[px]&0xffffff)==0;int low=solid?28:pass==0?80:pass==1?58:40,high=solid?244:pass==0?211:pass==1?227:238;
                pixels[px]=constrain(pixels[px],ink,low,high);
            }
            output.setPixels(pixels,0,r.size,r.x,r.y+py,r.size,1);
        }
    }
    static int constrain(int pixel,boolean ink,int low,int high){int r=(pixel>>16)&255,g=(pixel>>8)&255,b=pixel&255;double lum=.299*r+.587*g+.114*b;if(ink&&lum>low){double f=low/Math.max(1,lum);r=(int)Math.round(r*f);g=(int)Math.round(g*f);b=(int)Math.round(b*f);}else if(!ink&&lum<high){double f=(high-lum)/Math.max(1,255-lum);r=(int)Math.round(r+(255-r)*f);g=(int)Math.round(g+(255-g)*f);b=(int)Math.round(b+(255-b)*f);}return 0xff000000|r<<16|g<<8|b;}
}
