package com.guangyingruoshui.imagetools;

import java.io.*;
import java.util.*;
import com.google.zxing.LuminanceSource;
import com.google.zxing.common.BitMatrix;

/** Synthetic round finder/dotted module fixtures vary content, location and rotation. */
final class QrRoundedImportTests {
    static void require(boolean value,String message){if(!value)throw new AssertionError(message);}
    static final class Fixture {
        final int width,height;final byte[] pixels;
        Fixture(int w,int h){width=w;height=h;pixels=new byte[w*h];Arrays.fill(pixels,(byte)255);for(int y=20;y<65;y++)for(int x=20;x<Math.min(w-20,650);x++)pixels[y*w+x]=(byte)170;}
        void add(String text,int left,int top,int module,boolean round,boolean rotate)throws Exception{
            BitMatrix matrix=QrService.matrix(text,1,0,true);int n=matrix.getWidth(),edge=n*module;byte[] face=new byte[edge*edge];Arrays.fill(face,(byte)255);
            for(int y=0;y<edge;y++)for(int x=0;x<edge;x++){int mx=x/module,my=y/module;double dx=(x+.5)/module-mx-.5,dy=(y+.5)/module-my-.5;if(matrix.get(mx,my)&&(!round||dx*dx+dy*dy<.46*.46))face[y*edge+x]=50;}
            if(round)for(int[] finder:new int[][]{{0,0},{n-7,0},{0,n-7}}){
                int fx=finder[0],fy=finder[1];
                for(int y=Math.max(0,(fy-1)*module);y<Math.min(edge,(fy+8)*module);y++)for(int x=Math.max(0,(fx-1)*module);x<Math.min(edge,(fx+8)*module);x++){
                    double d=Math.hypot((x+.5)/module-fx-3.5,(y+.5)/module-fy-3.5);face[y*edge+x]=(byte)(d<1.5||d<3.5&&d>2.5?50:255);
                }
            }
            require(left>=0&&top>=0&&left+edge<=width&&top+edge<=height,"Fixture outside canvas");
            for(int y=0;y<edge;y++)for(int x=0;x<edge;x++){int rx=rotate?edge-y-1:x,ry=rotate?x:y;pixels[(top+ry)*width+left+rx]=face[y*edge+x];}
        }
        LuminanceSource source(){return new QrService.GraySource(width,height,pixels);}
    }
    static void run()throws Exception{
        String first="https://example.org/round-import",second="BEGIN:VCARD\r\nVERSION:3.0\r\nFN:测试\r\nEND:VCARD\r\n";
        for(boolean rotate:new boolean[]{false,true}){
            Fixture fixture=new Fixture(1500,2200);String value=rotate?second:first;fixture.add(value,340,740,16,true,rotate);
            List<QrService.Match> found=QrService.decodeImported(fixture.source(),new Files.Job());
            require(found.size()==1&&value.equals(found.get(0).payload),"Automatic rounded import lost exact data at a new position/rotation");
            QrService.Match match=found.get(0);int codeEdge=QrService.matrix(value,1,0,true).getWidth()*16;require(match.width==1500&&match.height==2200&&match.left>=0&&match.top>=0&&match.right<=1500&&match.bottom<=2200&&Math.abs((match.left+match.right)/2.-(340+codeEdge/2.))<codeEdge*.25&&Math.abs((match.top+match.bottom)/2.-(740+codeEdge/2.))<codeEdge*.25,"Recovered QR bounds did not map to source");
            require(value.equals(QrService.decode(QrScreenshotTests.code(match.payload,512,true))),"Recovered raw QR did not round-trip");
        }
        for(boolean mixed:new boolean[]{false,true}){
            Fixture fixture=new Fixture(1700,1300);fixture.add(first,120,380,12,!mixed,false);fixture.add(second,980,380,12,true,true);
            List<QrService.Match> found=QrService.decodeImported(fixture.source(),new Files.Job());Set<String> values=new HashSet<>();for(QrService.Match match:found)values.add(match.payload);
            require(found.size()==2&&values.contains(first)&&values.contains(second),"Mixed/double rounded codes were omitted or duplicated");
            require(Math.abs(found.get(0).left-found.get(1).left)>600,"Recovered multiple QR regions collapsed");
        }
        Fixture blank=new Fixture(1100,900);require(QrService.decodeImported(blank.source(),new Files.Job()).isEmpty(),"Non-QR screenshot produced data");
        Files.Job job=new Files.Job();job.cancelled=true;boolean stopped=false;try{QrRoundedImporter.recover(blank.source(),job);}catch(InterruptedIOException expected){stopped=true;}require(stopped,"Rounded import ignored cancellation");
    }
}
