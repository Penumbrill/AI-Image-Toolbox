package com.guangyingruoshui.imagetools;

import java.io.*;
import java.util.Arrays;
import com.google.zxing.LuminanceSource;
import com.google.zxing.common.BitMatrix;

// The production luminance decoder also runs on the host JVM without Android UI.
final class QrDecodeTests {
    static void require(boolean value,String message){if(!value)throw new AssertionError(message);}
    static LuminanceSource code(String url,int edge)throws Exception{
        BitMatrix matrix=QrService.matrix(url,edge,4);byte[] pixels=new byte[matrix.getWidth()*matrix.getHeight()];
        for(int y=0;y<matrix.getHeight();y++)for(int x=0;x<matrix.getWidth();x++)pixels[y*matrix.getWidth()+x]=(byte)(matrix.get(x,y)?0:255);
        return new QrService.GraySource(matrix.getWidth(),matrix.getHeight(),pixels);
    }
    static void run()throws Exception{
        require(Arrays.equals(QrService.scanEdges(1984,3008),new int[]{3008,1024,800,768,640,512}),"Bounded large-image retries");
        require(Arrays.equals(QrService.scanEdges(800,512),new int[]{800,768,640,512}),"No repeated original size");
        require(Arrays.equals(QrService.scanEdges(768,512),new int[]{768,640,512}),"No repeated 768 intermediate size");
        require(Arrays.equals(QrService.scanEdges(640,480),new int[]{640,512}),"No repeated 640 intermediate size");
        for(int edge:new int[]{1,256,512,639,640,767,768,799,800,1023,1024,3200}){int[] edges=QrService.scanEdges(edge,Math.max(1,edge/2));require(edges.length<=6&&edges[0]==edge,"Exceeded six scan scales");for(int k=1;k<edges.length;k++)require(edges[k]<edges[k-1]&&edges[k]<edge,"Scan repeated a size or upscaled input");}
        require(Arrays.equals(QrService.scanEdges(256,256),new int[]{256}),"No upscaling small QR images");
        String first="https://example.com/gallery/123?source=android",other="http://example.org/other-page";
        for(int edge:new int[]{256,800,2048})require(first.equals(QrService.decode(code(first,edge))),"Normal QR exact URL at "+edge);
        String decoded=QrService.decode(code(other,1200));require(other.equals(decoded)&&!first.equals(decoded),"Do not accept another URL as the expected one");
        for(String invalid:new String[]{"weixin://dl/business","javascript:alert(1)","https://user:password@example.com/","https://example.com/a b"}){
            boolean rejected=false;try{QrService.matrix(invalid,512,4);}catch(Exception e){rejected=true;}require(rejected,"Invalid website accepted: "+invalid);
        }
        byte[] blank=new byte[1536*2048];Arrays.fill(blank,(byte)255);LuminanceSource empty=new QrService.GraySource(1536,2048,blank);
        require(QrService.decode(empty)==null,"Blank image produced a false URL");
        LuminanceSource smaller=QrService.reduced(empty,800,new Files.Job());require(smaller.getWidth()==600&&smaller.getHeight()==800&&smaller.getMatrix().length==480000,"Resampling dimensions");
        require((smaller.getMatrix()[123]&255)==255,"Resampling changed uniform luminance");
        Files.Job cancelled=new Files.Job();cancelled.cancelled=true;boolean stopped=false;try{QrService.decode(empty,cancelled);}catch(InterruptedIOException e){stopped=true;}require(stopped,"Cancelled scan continued decoding");
        require(QrService.gray(0x00000000,255)==255&&QrService.gray(0x00000000,0)==0,"Transparent backgrounds");
        require(QrService.gray(0xffff0000,255)==63&&QrService.gray(0xff00ff00,0)==127,"RGB luminance parity");
    }
    public static void main(String[] args)throws Exception{long start=System.nanoTime();run();System.out.println("PASS: bounded multi-scale QR decoding, distinct URLs, invalid websites, blank image, transparent luminance and cancellation ("+((System.nanoTime()-start)/1000000)+" ms)");}
}
