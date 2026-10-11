package com.guangyingruoshui.imagetools;

import android.graphics.Bitmap;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;
import org.json.*;

/** Read the user's two private fixtures at explicit test paths; never ship their screenshots in APK assets. */
final class QrActualScreenshotTests {
    static void run(MainActivity a){
        File external=a.getExternalFilesDir(null),directory=new File(external==null?a.getFilesDir():external,"verification");directory.mkdirs();
        JSONObject report=new JSONObject();Bitmap source=null,encoded=null;
        try{
            Files.Job job=new Files.Job();JSONArray cases=new JSONArray();
            for(String name:new String[]{"qq","wechat"}){
                String path=a.getIntent().getStringExtra("qr-"+name);
                if(path==null)throw new IOException("Actual screenshot path omitted: "+name);
                File file=new File(path);String before=Files.hash(file,job);source=Images.load(file,0,true);
                List<QrService.Match> matches=QrService.scanImported(source,job);
                if(matches.size()!=1)throw new IOException("Expected one actual "+name+" screenshot QR, found "+matches.size());
                QrService.Match match=matches.get(0);
                if(match.left<0||match.top<0||match.right>source.getWidth()||match.bottom>source.getHeight()||match.left>=match.right||match.top>=match.bottom)throw new IOException("Invalid actual screenshot extraction bounds");
                encoded=QrService.code(match.payload,512,4,true);
                if(!QrService.matches(encoded,match.payload,job))throw new IOException("Actual screenshot payload changed when regenerated: "+name);
                if(!Files.hash(file,job).equals(before))throw new IOException("Screenshot source bytes changed");
                cases.put(new JSONObject().put("name",name).put("sourceSha256",before).put("width",source.getWidth()).put("height",source.getHeight()).put("matches",matches.size()).put("payloadSha256",Files.hex(MessageDigest.getInstance("SHA-256").digest(match.payload.getBytes(Files.UTF8)))).put("left",match.left).put("top",match.top).put("right",match.right).put("bottom",match.bottom).put("rawRegenerationPassed",true).put("sourceUnchanged",true));
                source.recycle();source=null;encoded.recycle();encoded=null;
            }
            report.put("cases",cases).put("passed",true).put("payloadsKeptPrivate",true);a.ui.post(()->a.message("ACTUAL SCREENSHOTS PASS"));
        }catch(Throwable failure){try{report.put("passed",false).put("error",failure.toString());}catch(Exception ignored){}a.ui.post(()->a.message(a.error(failure)));}
        finally{if(source!=null)source.recycle();if(encoded!=null)encoded.recycle();try(OutputStream out=new FileOutputStream(new File(directory,"actual-screenshots.json"))){out.write(report.toString(2).getBytes(Files.UTF8));}catch(Exception ignored){}}
    }
}
