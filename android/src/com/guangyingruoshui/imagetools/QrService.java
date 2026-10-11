package com.guangyingruoshui.imagetools;

import android.graphics.*;
import java.io.*;
import java.net.URI;
import java.util.*;
import com.google.zxing.*;
import com.google.zxing.common.*;
import com.google.zxing.qrcode.*;
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel;
import org.json.JSONObject;

final class QrService {
    static final class Region {String url;boolean raw;int x,y,size,width,height;int margin=4;}
    // Keep QR text exact: website normalization is only for explicitly opening a URL.
    static String payload(String value)throws IOException{if(value==null||value.isEmpty())throw new IOException("请输入二维码内容 / Enter QR content");if(value.getBytes(Files.UTF8).length>1100)throw new IOException("二维码内容过长，请缩短内容 / QR content is too long; use shorter content");return value;}
    static String content(String value,boolean raw)throws Exception{return raw?payload(value):website(value);}
    static final class Match {final String payload;final int left,top,right,bottom,width,height;Match(String value,int x0,int y0,int x1,int y1,int w,int h){payload=value;left=x0;top=y0;right=x1;bottom=y1;width=w;height=h;}}
    static String website(String value)throws Exception{URI u=new URI(value==null?"":value.trim());String scheme=u.getScheme();if(!("https".equalsIgnoreCase(scheme)||"http".equalsIgnoreCase(scheme))||u.getHost()==null||u.getUserInfo()!=null)throw new IOException("请输入完整的 HTTP 或 HTTPS 网站链接 / Enter a full HTTP or HTTPS website URL");String s=u.toASCIIString();if(s.getBytes(Files.UTF8).length>1100)throw new IOException("链接过长，请使用较短链接 / Use a shorter URL");return s;}
    static BitMatrix matrix(String value,int side)throws Exception{return matrix(value,side,4);}
    static BitMatrix matrix(String value,int side,int margin)throws Exception{return matrix(value,side,margin,false);}
    static BitMatrix matrix(String value,int side,int margin,boolean raw)throws Exception{if(margin<0||margin>12)throw new IOException("Invalid QR border");Map<EncodeHintType,Object> hints=new EnumMap<>(EncodeHintType.class);hints.put(EncodeHintType.ERROR_CORRECTION,ErrorCorrectionLevel.H);hints.put(EncodeHintType.CHARACTER_SET,"UTF-8");hints.put(EncodeHintType.MARGIN,margin);return new QRCodeWriter().encode(content(value,raw),BarcodeFormat.QR_CODE,side,side,hints);}
    static Bitmap code(String value,int side)throws Exception{return code(value,side,4);}
    static Bitmap code(String value,int side,int margin)throws Exception{return code(value,side,margin,false);}
    static Bitmap code(String value,int side,int margin,boolean raw)throws Exception{BitMatrix m=matrix(value,side,margin,raw);Bitmap image=Bitmap.createBitmap(m.getWidth(),m.getHeight(),Bitmap.Config.ARGB_8888);int[] row=new int[m.getWidth()];for(int y=0;y<m.getHeight();y++){for(int x=0;x<row.length;x++)row[x]=m.get(x,y)?Color.BLACK:Color.WHITE;image.setPixels(row,0,row.length,0,y,row.length,1);}return image;}
    static final class GraySource extends LuminanceSource {
        final byte[] values;
        GraySource(int width,int height,byte[] values){super(width,height);this.values=values;}
        public byte[] getRow(int y,byte[] row){if(y<0||y>=getHeight())throw new IllegalArgumentException("Invalid luminance row");if(row==null||row.length<getWidth())row=new byte[getWidth()];System.arraycopy(values,y*getWidth(),row,0,getWidth());return row;}
        public byte[] getMatrix(){return values;}
    }
    static void check(Files.Job job)throws IOException{if(job!=null)job.check();}
    // Fine artwork can alias differently on each sample grid. Keep retries bounded,
    // including common 768- and 640-pixel sizes between the 800 and 512 grids.
    static int[] scanEdges(int width,int height){int edge=Math.max(width,height),count=1;for(int size:new int[]{1024,800,768,640,512})if(size<edge)count++;int[] sizes=new int[count];sizes[0]=edge;int index=1;for(int size:new int[]{1024,800,768,640,512})if(size<edge)sizes[index++]=size;return sizes;}
    static LuminanceSource reduced(LuminanceSource source,int edge,Files.Job job)throws IOException{
        int sw=source.getWidth(),sh=source.getHeight();double scale=edge/(double)Math.max(sw,sh);int w=Math.max(1,(int)Math.round(sw*scale)),h=Math.max(1,(int)Math.round(sh*scale));byte[] input=source.getMatrix(),output=new byte[w*h];
        int[] x0=new int[w],x1=new int[w];double[] fraction=new double[w];for(int x=0;x<w;x++){double sx=Math.max(0,Math.min(sw-1,(x+.5)*sw/w-.5));x0[x]=(int)sx;x1[x]=Math.min(sw-1,x0[x]+1);fraction[x]=sx-x0[x];}
        for(int y=0;y<h;y++){check(job);double sy=Math.max(0,Math.min(sh-1,(y+.5)*sh/h-.5));int y0=(int)sy,y1=Math.min(sh-1,y0+1);double fy=sy-y0;for(int x=0;x<w;x++){double upper=(input[y0*sw+x0[x]]&255)*(1-fraction[x])+(input[y0*sw+x1[x]]&255)*fraction[x],lower=(input[y1*sw+x0[x]]&255)*(1-fraction[x])+(input[y1*sw+x1[x]]&255)*fraction[x];output[y*w+x]=(byte)Math.round(upper*(1-fy)+lower*fy);}}
        return new GraySource(w,h,output);
    }
    static String decodeAtScale(LuminanceSource source,Files.Job job)throws IOException{
        Map<DecodeHintType,Object> hints=new EnumMap<>(DecodeHintType.class);hints.put(DecodeHintType.TRY_HARDER,true);hints.put(DecodeHintType.POSSIBLE_FORMATS,Collections.singletonList(BarcodeFormat.QR_CODE));
        for(boolean inverse:new boolean[]{false,true}){check(job);LuminanceSource attempt=inverse?source.invert():source;try{return new MultiFormatReader().decode(new BinaryBitmap(new HybridBinarizer(attempt)),hints).getText();}catch(NotFoundException ignored){check(job);try{Result[] values=new com.google.zxing.multi.qrcode.QRCodeMultiReader().decodeMultiple(new BinaryBitmap(new HybridBinarizer(attempt)),hints);if(values.length>0)return values[0].getText();}catch(NotFoundException none){}}}
        return null;
    }
    static String decode(LuminanceSource source)throws IOException{return decode(source,null);}
    static String decode(LuminanceSource source,Files.Job job)throws IOException{
        for(int edge:scanEdges(source.getWidth(),source.getHeight())){check(job);LuminanceSource attempt=edge==Math.max(source.getWidth(),source.getHeight())?source:reduced(source,edge,job);String text=decodeAtScale(attempt,job);if(text!=null)return text;}
        return null;
    }
    static Match detected(Result result,int width,int height,int originalWidth,int originalHeight){
        ResultPoint[] points=result.getResultPoints();float left=width,top=height,right=0,bottom=0;int count=0;
        if(points!=null)for(ResultPoint point:points)if(point!=null){left=Math.min(left,point.getX());top=Math.min(top,point.getY());right=Math.max(right,point.getX());bottom=Math.max(bottom,point.getY());count++;}
        // Bounds are an approximate selection aid. Only decoded text is transferred.
        if(count==0){left=top=0;right=width;bottom=height;}else{float padding=Math.max(right-left,bottom-top)*.25f;left-=padding;top-=padding;right+=padding;bottom+=padding;}
        double sx=originalWidth/(double)width,sy=originalHeight/(double)height;
        return new Match(result.getText(),Math.max(0,(int)Math.floor(left*sx)),Math.max(0,(int)Math.floor(top*sy)),Math.min(originalWidth,(int)Math.ceil(right*sx)),Math.min(originalHeight,(int)Math.ceil(bottom*sy)),originalWidth,originalHeight);
    }
    static void addMatch(List<Match> matches,Match value){
        if(value.payload==null||value.payload.isEmpty())return;
        for(Match old:matches)if(old.payload.equals(value.payload)){double dx=(old.left+old.right-value.left-value.right)/2.,dy=(old.top+old.bottom-value.top-value.bottom)/2.,size=Math.min(Math.max(old.right-old.left,old.bottom-old.top),Math.max(value.right-value.left,value.bottom-value.top));if(Math.hypot(dx,dy)<=Math.max(4,size*.25))return;}
        if(matches.size()<32)matches.add(value);
    }
    static List<Match> decodeAll(LuminanceSource source,Files.Job job)throws IOException{
        List<Match> matches=new ArrayList<>();Map<DecodeHintType,Object> hints=new EnumMap<>(DecodeHintType.class);hints.put(DecodeHintType.TRY_HARDER,true);hints.put(DecodeHintType.POSSIBLE_FORMATS,Collections.singletonList(BarcodeFormat.QR_CODE));
        int width=source.getWidth(),height=source.getHeight();
        for(int edge:scanEdges(width,height)){
            check(job);LuminanceSource scaled=edge==Math.max(width,height)?source:reduced(source,edge,job);
            for(boolean inverse:new boolean[]{false,true}){
                check(job);LuminanceSource attempt=inverse?scaled.invert():scaled;BinaryBitmap bitmap=new BinaryBitmap(new HybridBinarizer(attempt));
                try{for(Result result:new com.google.zxing.multi.qrcode.QRCodeMultiReader().decodeMultiple(bitmap,hints)){check(job);addMatch(matches,detected(result,scaled.getWidth(),scaled.getHeight(),width,height));}}
                catch(NotFoundException ignored){check(job);try{Result result=new MultiFormatReader().decode(bitmap,hints);addMatch(matches,detected(result,scaled.getWidth(),scaled.getHeight(),width,height));}catch(NotFoundException none){}}
                if(matches.size()>=32)break;
            }
            if(matches.size()>=32)break;
        }
        Collections.sort(matches,(a,b)->a.top==b.top?Integer.compare(a.left,b.left):Integer.compare(a.top,b.top));return matches;
    }
    // This recovery belongs to screenshot import. Generated QR self-checks use decodeAll.
    static List<Match> decodeImported(LuminanceSource source,Files.Job job)throws IOException{List<Match> matches=decodeAll(source,job);for(Match value:QrRoundedImporter.recover(source,job))addMatch(matches,value);Collections.sort(matches,(a,b)->a.top==b.top?Integer.compare(a.left,b.left):Integer.compare(a.top,b.top));return matches;}
    static int gray(int color,int background){int alpha=color>>>24,red=((color>>>16&255)*alpha+background*(255-alpha)+127)/255,green=((color>>>8&255)*alpha+background*(255-alpha)+127)/255,blue=((color&255)*alpha+background*(255-alpha)+127)/255;return (red+2*green+blue)/4;}
    static LuminanceSource luminance(Bitmap image,int background,Files.Job job)throws IOException{
        int sw=image.getWidth(),sh=image.getHeight();double scale=Math.min(1,3200.0/Math.max(sw,sh));int w=Math.max(1,(int)Math.round(sw*scale)),h=Math.max(1,(int)Math.round(sh*scale));byte[] values=new byte[w*h];int[] upper=new int[sw],lower=new int[sw],x0=new int[w],x1=new int[w];double[] fraction=new double[w];
        for(int x=0;x<w;x++){double sx=Math.max(0,Math.min(sw-1,(x+.5)*sw/w-.5));x0[x]=(int)sx;x1[x]=Math.min(sw-1,x0[x]+1);fraction[x]=sx-x0[x];}
        for(int y=0;y<h;y++){check(job);double sy=Math.max(0,Math.min(sh-1,(y+.5)*sh/h-.5));int y0=(int)sy,y1=Math.min(sh-1,y0+1);double fy=sy-y0;image.getPixels(upper,0,sw,0,y0,sw,1);if(fy>0)image.getPixels(lower,0,sw,0,y1,sw,1);for(int x=0;x<w;x++){double top=gray(upper[x0[x]],background)*(1-fraction[x])+gray(upper[x1[x]],background)*fraction[x],bottom=fy==0?top:gray(lower[x0[x]],background)*(1-fraction[x])+gray(lower[x1[x]],background)*fraction[x];values[y*w+x]=(byte)Math.round(top*(1-fy)+bottom*fy);}}
        return new GraySource(w,h,values);
    }
    static String scan(Bitmap image,Files.Job job)throws Exception{for(int background:new int[]{255,0}){check(job);String text=decode(luminance(image,background,job),job);if(text!=null)return text;if(!image.hasAlpha())break;}return null;}
    static List<Match> scanAll(Bitmap image,Files.Job job)throws Exception{List<Match> matches=new ArrayList<>();for(int background:new int[]{255,0}){check(job);for(Match value:decodeAll(luminance(image,background,job),job))addMatch(matches,value);if(!image.hasAlpha())break;}Collections.sort(matches,(a,b)->a.top==b.top?Integer.compare(a.left,b.left):Integer.compare(a.top,b.top));return matches;}
    static List<Match> scanImported(Bitmap image,Files.Job job)throws Exception{List<Match> matches=new ArrayList<>();for(int background:new int[]{255,0}){check(job);LuminanceSource source=luminance(image,background,job);double sx=image.getWidth()/(double)source.getWidth(),sy=image.getHeight()/(double)source.getHeight();for(Match value:decodeImported(source,job))addMatch(matches,new Match(value.payload,(int)Math.floor(value.left*sx),(int)Math.floor(value.top*sy),Math.min(image.getWidth(),(int)Math.ceil(value.right*sx)),Math.min(image.getHeight(),(int)Math.ceil(value.bottom*sy)),image.getWidth(),image.getHeight()));if(!image.hasAlpha())break;}Collections.sort(matches,(a,b)->a.top==b.top?Integer.compare(a.left,b.left):Integer.compare(a.top,b.top));return matches;}
    static boolean matches(Bitmap image,String expected,Files.Job job)throws Exception{for(Match match:scanAll(image,job))if(expected.equals(match.payload))return true;return false;}
    static Region compose(Bitmap result,String url,int percent,int x,int y,boolean only)throws Exception{return compose(result,url,percent,x,y,only,4);}
    static Region position(Bitmap result,String url,int percent,int x,int y,int margin)throws Exception{return position(result,url,percent,x,y,margin,false);}
    static Region position(Bitmap result,String url,int percent,int x,int y,int margin,boolean raw)throws Exception{url=content(url,raw);int edge=Math.min(result.getWidth(),result.getHeight()),modules=matrix(url,1,margin,raw).getWidth(),requested=edge*Math.max(10,Math.min(90,percent))/100,scale=Math.max(4,requested/modules),side=modules*scale;if(side>edge)throw new IOException("图片太小，请使用更大图片 / Use a larger image");Region r=new Region();r.raw=raw;r.margin=margin;r.url=url;r.size=side;r.width=result.getWidth();r.height=result.getHeight();r.x=Math.max(0,Math.min(r.width-side,Math.round(r.width*x/100f-side/2f)));r.y=Math.max(0,Math.min(r.height-side,Math.round(r.height*y/100f-side/2f)));return r;}
    static Region compose(Bitmap result,String url,int percent,int x,int y,boolean only,int margin)throws Exception{return compose(result,url,percent,x,y,only,margin,false);}
    static Region compose(Bitmap result,String url,int percent,int x,int y,boolean only,int margin,boolean raw)throws Exception{Region r=position(result,url,percent,x,y,margin,raw);Bitmap qr=code(r.url,r.size,margin,raw);try{new Canvas(result).drawBitmap(qr,r.x,r.y,new Paint());}finally{qr.recycle();}return r;}
    static int optimizePixel(int cover,boolean ink,BackgroundRevealAuto.Profile profile){double luma=BackgroundRevealAuto.y(cover);int white=Math.max(181,Math.min(255,BackgroundRevealAuto.channel(BackgroundRevealAuto.tone(luma,profile.whiteLow,profile.whiteHigh,profile.whiteFloor,250)))),black=ink?16:112,alpha=BackgroundRevealAuto.channel(255*(1-(white-black)/250.0)),c=alpha==0?0:BackgroundRevealAuto.channel(black*255.0/alpha);return alpha<<24|c<<16|c<<8|c;}
    static Region optimize(Bitmap result,Region input,Bitmap cover,BackgroundRevealAuto.Profile profile,Files.Job job,boolean strict)throws Exception{
        job.check();if(input==null||cover==null||profile==null)throw new IOException("二维码优化需要本工具生成的二维码 PNG / QR optimization requires an app-generated QR PNG");double sx=result.getWidth()/(double)input.width,sy=result.getHeight()/(double)input.height;int modules=matrix(input.url,1,input.margin,input.raw).getWidth(),scale=(int)Math.round(input.size*Math.min(sx,sy))/modules;if(strict&&scale<4)throw new IOException("导出尺寸太小，请提高二维码分辨率 / Increase output size to keep the QR scannable");scale=Math.max(1,scale);int side=scale*modules;
        Region r=new Region();r.raw=input.raw;r.margin=input.margin;r.url=input.url;r.size=Math.max(side,(int)Math.ceil(input.size*Math.max(sx,sy)));r.width=result.getWidth();r.height=result.getHeight();if(r.size>Math.min(r.width,r.height))throw new IOException("QR output too small");r.x=Math.max(0,Math.min(r.width-r.size,(int)Math.floor(input.x*sx)));r.y=Math.max(0,Math.min(r.height-r.size,(int)Math.floor(input.y*sy)));BitMatrix qr=matrix(r.url,side,r.margin,r.raw);Bitmap fitted=Images.fit(cover,r.width,r.height,Color.WHITE);
        try{int[] line=new int[result.getWidth()];for(int y=0;y<result.getHeight();y++){job.check();result.getPixels(line,0,line.length,0,y,line.length,1);for(int x=0;x<line.length;x++){int v=line[x],a=v>>>24,black=(((v>>>16)&255)*a+127)/255,white=black+255-a,alpha=BackgroundRevealAuto.channel(255*(1-(white-black)/250.0)),c=alpha==0?0:BackgroundRevealAuto.channel(black*255.0/alpha);line[x]=alpha<<24|c<<16|c<<8|c;}result.setPixels(line,0,line.length,0,y,line.length,1);}int[] pixels=new int[r.size],values=new int[r.size];int inset=(r.size-side)/2;for(int y=0;y<r.size;y++){job.check();fitted.getPixels(values,0,r.size,r.x,r.y+y,r.size,1);boolean inside=y>=inset&&y<inset+side;for(int x=0;x<r.size;x++){boolean ink=inside&&x>=inset&&x<inset+side&&qr.get(x-inset,y-inset);pixels[x]=optimizePixel(values[x],ink,profile);}result.setPixels(pixels,0,r.size,r.x,r.y+y,r.size,1);}}finally{fitted.recycle();}
        if(strict&&!matches(result,content(r.url,r.raw),job))throw new IOException("二维码优化自检失败，请增加尺寸 / QR optimization self-check failed; increase output size");return r;
    }
    static byte[] data(Region r)throws Exception{return new JSONObject().put("Url",r.url).put("Raw",r.raw).put("X",r.x).put("Y",r.y).put("Size",r.size).put("Width",r.width).put("Height",r.height).put("Margin",r.margin).toString().getBytes(Files.UTF8);}
    static void tag(File png,Region r,Files.Job job)throws Exception{if(r==null)return;File tmp=Files.temp(png.getParentFile());try(InputStream in=new FileInputStream(png);OutputStream out=new FileOutputStream(tmp)){out.write(Files.read(in,8));while(true){job.check();byte[] head=Files.read(in,8);long length=((long)(head[0]&255)<<24)|((long)(head[1]&255)<<16)|((long)(head[2]&255)<<8)|(head[3]&255);String type=new String(head,4,4,"US-ASCII");if(type.equals("IEND"))Files.chunk(out,"qrLK",data(r));if(type.equals("qrLK")){skip(in,length+4);continue;}out.write(head);copyCount(in,out,length+4,job);if(type.equals("IEND"))break;}}catch(Exception e){tmp.delete();throw e;}try{if(!png.delete()||!tmp.renameTo(png))throw new IOException("Unable to update QR metadata");}finally{tmp.delete();}}
    static void copyCount(InputStream in,OutputStream out,long count,Files.Job job)throws Exception{byte[] b=new byte[65536];while(count>0){job.check();int n=in.read(b,0,(int)Math.min(count,b.length));if(n<0)throw new EOFException();out.write(b,0,n);count-=n;}}
    static void skip(InputStream in,long n)throws IOException{while(n>0){long skipped=in.skip(n);if(skipped<=0){if(in.read()<0)throw new EOFException();skipped=1;}n-=skipped;}}
    static Region read(File png)throws Exception{if(!Files.ext(png.getName()).equals(".png"))return null;try(InputStream in=new FileInputStream(png)){byte[] sig=Files.read(in,8);if(!Arrays.equals(sig,Files.PNG))return null;int width=0,height=0;while(true){byte[] h=Files.read(in,8);long n=((long)(h[0]&255)<<24)|((long)(h[1]&255)<<16)|((long)(h[2]&255)<<8)|(h[3]&255);String type=new String(h,4,4,"US-ASCII");if(type.equals("IHDR")&&n==13){byte[] dimensions=Files.read(in,13);width=((dimensions[0]&255)<<24)|((dimensions[1]&255)<<16)|((dimensions[2]&255)<<8)|(dimensions[3]&255);height=((dimensions[4]&255)<<24)|((dimensions[5]&255)<<16)|((dimensions[6]&255)<<8)|(dimensions[7]&255);skip(in,4);continue;}if(type.equals("qrLK")){if(n>8192)return null;byte[] data=Files.read(in,(int)n);java.util.zip.CRC32 crc=new java.util.zip.CRC32();crc.update(Arrays.copyOfRange(h,4,8));crc.update(data);byte[] checksum=Files.read(in,4);long expected=((long)(checksum[0]&255)<<24)|((long)(checksum[1]&255)<<16)|((long)(checksum[2]&255)<<8)|(checksum[3]&255);if(crc.getValue()!=expected)return null;JSONObject o=new JSONObject(new String(data,Files.UTF8));Region r=new Region();r.raw=o.optBoolean("Raw",false);r.url=content(o.getString("Url"),r.raw);r.x=o.getInt("X");r.y=o.getInt("Y");r.size=o.getInt("Size");r.width=o.getInt("Width");r.height=o.getInt("Height");r.margin=o.optInt("Margin",4);if(r.margin<0||r.margin>12||r.width!=width||r.height!=height||r.width<1||r.height<1||r.width>8192||r.height>8192||r.size<1||r.x<0||r.y<0||(long)r.x+r.size>r.width||(long)r.y+r.size>r.height)return null;return r;}if(type.equals("IEND"))return null;skip(in,n+4);}}catch(Exception e){return null;}}
}
