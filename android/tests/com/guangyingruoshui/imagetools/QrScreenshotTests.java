package com.guangyingruoshui.imagetools;

import java.io.*;
import java.util.*;
import com.google.zxing.LuminanceSource;
import com.google.zxing.common.BitMatrix;

// Host JVM regression exercises production text/QR logic, not Android UI stubs.
final class QrScreenshotTests {
    static void require(boolean value,String message){if(!value)throw new AssertionError(message);}
    static LuminanceSource code(String text,int edge,boolean raw)throws Exception{
        BitMatrix bits=QrService.matrix(text,edge,4,raw);byte[] pixels=new byte[bits.getWidth()*bits.getHeight()];
        for(int y=0;y<bits.getHeight();y++)for(int x=0;x<bits.getWidth();x++)pixels[y*bits.getWidth()+x]=(byte)(bits.get(x,y)?0:255);
        return new QrService.GraySource(bits.getWidth(),bits.getHeight(),pixels);
    }
    static LuminanceSource screenshot(String first,String second)throws Exception{
        int width=1600,height=1100;byte[] pixels=new byte[width*height];Arrays.fill(pixels,(byte)255);
        // Surrounding UI-like bars and a small avatar-shaped block are not QR data.
        for(int y=25;y<85;y++)for(int x=30;x<1450;x++)pixels[y*width+x]=(byte)155;
        for(int y=135;y<215;y++)for(int x=50;x<130;x++)pixels[y*width+x]=(byte)50;
        for(int y=155;y<175;y++)for(int x=180;x<620;x++)pixels[y*width+x]=(byte)120;
        for(int index=0;index<2;index++){
            LuminanceSource qr=code(index==0?first:second,400,true);byte[] values=qr.getMatrix();int left=index==0?150:950,top=350;
            for(int y=0;y<qr.getHeight();y++)System.arraycopy(values,y*qr.getWidth(),pixels,(top+y)*width+left,qr.getWidth());
        }
        return new QrService.GraySource(width,height,pixels);
    }
    static void run()throws Exception{
        String vcard="BEGIN:VCARD\r\nVERSION:3.0\r\nFN:测试用户\r\nEMAIL:test@example.org\r\nEND:VCARD\r\n";
        String text="  原样文本 😀\r\n下一行\t尾部  ",unsafe="weixin://dl/business/test-only",url="https://example.com/people/42?mode=qr";
        for(String value:new String[]{vcard,text,unsafe,"javascript:alert('test-only')",url}){
            require(QrService.payload(value).equals(value),"Raw payload changed before encoding");
            require(value.equals(QrService.decode(code(value,512,true))),"Raw content did not round-trip exactly");
        }
        String legacy="  https://example.com/目录?q=测试  ";String canonical=QrService.website(legacy);
        require(!legacy.equals(canonical)&&canonical.equals(QrService.content(legacy,false)),"Legacy URL canonicalization changed");
        require(legacy.equals(QrService.content(legacy,true)),"Imported raw URL was normalized");
        require(canonical.equals(QrService.decode(code(legacy,512,false))),"Legacy website generation changed");
        for(String value:new String[]{vcard,text,unsafe,"javascript:alert('test-only')","https://user:password@example.com/"}){
            boolean blocked=false;try{QrService.website(value);}catch(Exception expected){blocked=true;}require(blocked,"Nonwebsite content became an openable URL");
        }
        for(String value:new String[]{null,"",String.join("",Collections.nCopies(1101,"a")),String.join("",Collections.nCopies(367,"中"))}){
            boolean blocked=false;try{QrService.payload(value);}catch(IOException expected){blocked=true;}require(blocked,"Empty/oversized raw content accepted");
        }
        List<QrService.Match> found=QrService.decodeAll(screenshot(url,vcard),new Files.Job());
        require(found.size()==2,"Screenshot lost a QR or retained duplicates across scales");
        Set<String> values=new HashSet<>();for(QrService.Match match:found){values.add(match.payload);require(match.width==1600&&match.height==1100&&match.left>=0&&match.top>=0&&match.right<=1600&&match.bottom<=1100&&match.right>match.left&&match.bottom>match.top,"QR selection bounds left source canvas");}
        require(values.contains(url)&&values.contains(vcard),"Multi-QR screenshot replaced exact selected content");
        found=QrService.decodeAll(screenshot(url,url),new Files.Job());
        require(found.size()==2,"Different QR regions with the same content were silently merged");
        require(Math.abs(found.get(0).left-found.get(1).left)>600,"Same-content QR locations collapsed");
        byte[] blank=new byte[1024*1024];Arrays.fill(blank,(byte)255);LuminanceSource empty=new QrService.GraySource(1024,1024,blank);
        require(QrService.decodeAll(empty,new Files.Job()).isEmpty(),"Non-QR screenshot produced a valid code");
        Files.Job cancelled=new Files.Job();cancelled.cancelled=true;boolean stopped=false;try{QrService.decodeAll(empty,cancelled);}catch(InterruptedIOException expected){stopped=true;}require(stopped,"Screenshot import ignored cancellation");
        QrRoundedImportTests.run();
    }
    public static void main(String[] args)throws Exception{long started=System.nanoTime();run();System.out.println("PASS: raw UTF-8/vCard exact round-trip, legacy website mode, safe URL opening, multi-QR selection, duplicate-region handling and cancellation ("+((System.nanoTime()-started)/1000000)+" ms)");}
}
