package com.guangyingruoshui.imagetools;
import android.graphics.*;
import java.io.*;
import java.util.*;
final class TransmissionChecks {
    static List<String> payloads(Bitmap image,Files.Job job)throws Exception{List<String> result=new ArrayList<>();for(QrService.Match match:QrService.scanAll(image,job))result.add(match.payload);Collections.sort(result);return result;}
    static void pick(MainActivity a){a.pick("image/*",false,files->{if(files.isEmpty())return;File input=files.get(0);String key=a.state[1].scramble.Key;String[] report={""};a.task(job->{report[0]=report(input,key,job,a.english);return Collections.emptyList();},outputs->new android.app.AlertDialog.Builder(a).setTitle(a.t("压缩模拟检查","Compression simulation")).setMessage(report[0]).setPositiveButton(a.t("关闭","Close"),null).show());});}
    static String report(File input,String key,Files.Job job,boolean english)throws Exception{
        Bitmap image=Images.load(input,0,true);try{int w=image.getWidth(),h=image.getHeight();int[] original=new int[w*h];image.getPixels(original,0,w,0,0,w,h);boolean alpha=false;for(int p:original)if((p>>>24)!=255){alpha=true;break;}
            ScrambleEngine.Options options=ScrambleEngine.read(input);int[] plain=null;if(options!=null){options.Key=key;plain=ScrambleEngine.transform(original.clone(),w,h,options,true,job);}
            List<String> expected=payloads(image,job);StringBuilder text=new StringBuilder(english?"Local simulation only; platform processing may differ.\n":"仅为本地模拟，平台处理可能不同。\n");
            if(alpha)text.append(english?"JPEG removes transparency; background reveal is lost.\n":"JPG 去除透明度，背景显图结构丢失。\n");
            try(RandomAccessFile file=new RandomAccessFile(input,"r")){if(file.length()>=8){byte[] signature=new byte[8];file.readFully(signature);if(Arrays.equals(signature,Files.PNG)){while(file.getFilePointer()+12<=file.length()){long length=file.readInt()&0xffffffffL;byte[] type=new byte[4];file.readFully(type);String name=new String(type,Files.LATIN);if(name.equals("acTL")){text.append(english?"JPEG loses dynamic dual-image frames.\n":"JPG 丢失动态双图帧。\n");break;}if(name.equals("IEND")||length>file.length()-file.getFilePointer()-4)break;file.seek(file.getFilePointer()+length+4);}}}}
            text.append(english?"Image re-encoding removes appended original files and parameter records.\n":"图片重编码丢失附带原文件和参数记录。\n");
            for(int i=0;i<4;i++){job.check();Bitmap sample=null;try{
                if(i<2){Bitmap white=Images.fit(image,w,h,Color.WHITE);try{ByteArrayOutputStream out=new ByteArrayOutputStream();if(!white.compress(Bitmap.CompressFormat.JPEG,i==0?85:75,out))throw new IOException("JPEG encode failed");byte[] bytes=out.toByteArray();sample=BitmapFactory.decodeByteArray(bytes,0,bytes.length);}finally{white.recycle();}}
                else sample=Bitmap.createScaledBitmap(image,Math.max(1,Math.round(w*(i==2?.75f:.5f))),Math.max(1,Math.round(h*(i==2?.75f:.5f))),true);
                if(sample==null)throw new IOException("Decode failed");text.append(i<2?"JPG "+(i==0?85:75)+"%: ":"Resize "+(i==2?75:50)+"%: ");
                if(!expected.isEmpty())text.append(english?"QR exact match ":"二维码内容一致 ").append(expected.equals(payloads(sample,job))?"PASS":"FAIL");else text.append(english?"no source QR detected":"源图未识别到二维码");
                if(plain!=null){if(i>=2)text.append(english?"; dimensions changed, exact recovery unavailable":"；尺寸改变，不能精确还原");else{int[] pixels=new int[w*h];sample.getPixels(pixels,0,w,0,0,w,h);pixels=ScrambleEngine.transform(pixels,w,h,options,true,job);double error=0;for(int p=0;p<pixels.length;p++){if((p&65535)==0)job.check();error+=ScrambleEngine.diff(pixels[p],plain[p]);}text.append(english?"; restored RGB mean error ":"；还原 RGB 平均误差 ").append(String.format(Locale.ROOT,"%.2f",error/(pixels.length*3.0)));}}
                text.append('\n');
            }finally{if(sample!=null&&sample!=image)sample.recycle();}}return text.toString();
        }finally{image.recycle();}
    }
}
