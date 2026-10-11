package com.guangyingruoshui.imagetools;

import android.graphics.*;
import android.util.Log;
import org.json.*;
import java.io.*;

/** Fast diagnostics on a previous, genuine native result; never substitutes for fresh inference. */
final class AiArtTests {
    static void run(MainActivity a){
        File external=a.getExternalFilesDir(null),dir=new File(external==null?a.getFilesDir():external,"ai-art");JSONObject report=new JSONObject();
        Bitmap source=null,previous=null,output=null,guide=null;
        try {
            Files.Job job=new Files.Job();source=Images.load(new File(dir,"source.png"),0,true);previous=Images.load(new File(dir,"previous-native-result.png"),0,true);output=previous.copy(Bitmap.Config.ARGB_8888,true);
            String payload=AiIntegrationTests.PAYLOAD;QrService.Region r=QrService.position(source,payload,38,20,75,4,false);
            AiVision.MaterialMap materials=new AiVision.MaterialMap(source.getWidth(),source.getHeight(),new byte[source.getWidth()*source.getHeight()]);
            QrAiLuminance.prepare(output,source,r,job);Images.encode(output,new File(dir,"prepared.png"),0,job);
            boolean full=QrService.matches(output,payload,job),reduced=full&&QrArt.reducedMatches(output,payload,job);
            boolean refined=!(full&&reduced)&&QrAiLuminance.refine(output,source,r,materials,false,job);
            Images.encode(output,new File(dir,"circular-diagnostic.png"),0,job);
            int modules=QrService.matrix(payload,1,r.margin,false).getWidth(),scale=r.size/modules,data=modules-r.margin*2;long outsideChanged=0,backingChanged=0;
            int[] before=new int[source.getWidth()],after=new int[source.getWidth()];
            for(int y=0;y<source.getHeight();y++){
                source.getPixels(before,0,before.length,0,y,before.length,1);output.getPixels(after,0,after.length,0,y,after.length,1);
                for(int x=0;x<before.length;x++)if(before[x]!=after[x]){
                    if(x<r.x||y<r.y||x>=r.x+r.size||y>=r.y+r.size)outsideChanged++;
                    else{double mx=(x-r.x+.5)/scale-r.margin,my=(y-r.y+.5)/scale-r.margin;int point=QrAiLuminance.locatorSample(mx,my,data);boolean quiet=mx<0||my<0||mx>=data||my>=data;if(point<0&&(quiet||QrAiLuminance.locatorArea(mx,my,data)))backingChanged++;}
                }
            }
            int side=Math.round(r.size*1.1f),left=r.x+(r.size-side)/2,top=r.y+(r.size-side)/2;
            guide=QrAiGeometry.guide(source,r,384,left,top,side,job);Images.encode(guide,new File(dir,"material-guide.png"),0,job);
            report.put("diagnostic_only",true).put("fresh_native_inference",false).put("starting_image","previous genuine native result with old calibration").put("full_decode",QrService.matches(output,payload,job)).put("reduced_decode",QrArt.reducedMatches(output,payload,job)).put("refined",refined).put("unchanged_outside_region",outsideChanged==0).put("unchanged_quiet_and_locator_backing",backingChanged==0).put("source_hash",Files.hash(new File(dir,"source.png"),job)).put("output_hash",Files.hash(new File(dir,"circular-diagnostic.png"),job));
            if(outsideChanged!=0||backingChanged!=0)throw new IOException("Source backing or outside pixels changed");
            String local=AiVision.materialPrompt("a girl in a school uniform","pink trees in front of tall city buildings","sd15",false);
            if(local.contains("garment")||local.contains("fabric")||!local.contains("building facades")||!local.contains("branches"))throw new IOException("Global clothing description leaks into background prompt");
            report.put("background_prompt",local).put("prompt_region_regression",true);
            Log.i("AIPT-ART","ART_DIAGNOSTIC_COMPLETE "+report.toString());
        }catch(Throwable error){try{report.put("error",error.toString());}catch(Exception ignored){}Log.e("AIPT-ART","ART_DIAGNOSTIC_FAILED",error);}
        finally {
            if(source!=null)source.recycle();if(previous!=null)previous.recycle();if(output!=null)output.recycle();if(guide!=null)guide.recycle();
            try{dir.mkdirs();AiIntegrationTests.write(new File(dir,"report.json"),report);}catch(Exception error){Log.e("AIPT-ART","Saving diagnostics",error);}
        }
    }
}
