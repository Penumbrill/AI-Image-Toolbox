package com.guangyingruoshui.imagetools;

import android.graphics.*;
import android.util.Log;
import com.google.zxing.*;
import com.google.zxing.common.HybridBinarizer;
import com.google.zxing.qrcode.QRCodeReader;
import org.json.JSONObject;
import java.io.*;
import java.util.*;

/** Explicit opt-in, test-APK-only end-to-end verification of the production AI action. */
final class AiIntegrationTests {
    static final String PAYLOAD="https://example.org/offline-qr";
    static void require(boolean ok,String message){if(!ok)throw new IllegalStateException(message);}
    static void log(String message){Log.i("AIPT-AI-Test",message);}
    static void captureNative(MainActivity a,int attempt,Bitmap initial,Bitmap guide,Bitmap mask,Bitmap generated){
        capture(a,"attempt-"+attempt+"-initial.png",initial);capture(a,"attempt-"+attempt+"-control.png",guide);capture(a,"attempt-"+attempt+"-mask.png",mask);capture(a,"attempt-"+attempt+"-native-patch.png",generated);
    }
    static void captureRaw(MainActivity a,int attempt,Bitmap result){capture(a,"attempt-"+attempt+"-raw-canvas.png",result);}
    static void capture(MainActivity a,String name,Bitmap bitmap){
        if(!a.getIntent().getBooleanExtra("aiintegration",false)||!a.getIntent().getBooleanExtra("captureRaw",false))return;
        try{File external=a.getExternalFilesDir(null),dir=new File(external==null?a.getFilesDir():external,"ai-integration/raw");require(dir.isDirectory()||dir.mkdirs(),"Cannot create native diagnostic folder");Images.encode(bitmap,new File(dir,name),0,new Files.Job());log("RAW_SAVED "+name+" "+bitmap.getWidth()+" x "+bitmap.getHeight());}catch(Exception error){throw new IllegalStateException("Unable to save genuine native diagnostics",error);}
    }
    static void run(MainActivity a){
        File reportFile=null;JSONObject report=new JSONObject();long started=System.currentTimeMillis();
        try {
            File external=a.getExternalFilesDir(null);File dir=new File(external==null?a.getFilesDir():external,"ai-integration");
            require(dir.isDirectory()||dir.mkdirs(),"Cannot create integration test folder");reportFile=new File(dir,"report.json");
            AiModelStore store=new AiModelStore(a);
            AiQrSettings settings=new AiQrSettings();settings.family="sd15";settings.autoPlace=false;settings.manualPlace=true;settings.autoPrompt=true;settings.background=false;settings.edge=a.getIntent().getIntExtra("aiEdge",384);settings.steps=a.getIntent().getIntExtra("aiSteps",24);settings.cfg=a.getIntent().getFloatExtra("aiCfg",settings.cfg);settings.strength=a.getIntent().getFloatExtra("aiStrength",settings.strength);settings.controlStrength=a.getIntent().getFloatExtra("aiControl",settings.controlStrength);settings.threads=4;settings.seed=17371;settings.clipSkip=0;
            for(AiModelStore.Resource resource:store.select(a.aiController.targets(settings))){File path=store.path(resource.target,resource.local);File parent=path.getParentFile();require(parent.isDirectory()||parent.mkdirs(),"Cannot create app-owned model target");}
            report.put("model_root",store.root.getAbsolutePath());report.put("test_directory",dir.getAbsolutePath());report.put("device","API "+android.os.Build.VERSION.SDK_INT+" "+Arrays.toString(android.os.Build.SUPPORTED_ABIS));
            if(a.getIntent().getBooleanExtra("provisionOnly",false)){report.put("provision_ready",true);write(reportFile,report);log("AI_PROVISION_READY "+store.root.getAbsolutePath());return;}
            File input=new File(dir,"complex-input.png");require(input.isFile(),"Safe complex test source missing");Bitmap original=Images.load(input,0,true);require(original.getWidth()==1920&&original.getHeight()==1280,"Complex source must be original 1920 x 1280");
            try {
                UiTests.ui(a,()->{MainActivity.State state=a.state[9];state.cover=input;state.qrMode=3;state.qrUrl=PAYLOAD;state.qrRaw=false;state.qrSize=38;state.qrX=20;state.qrY=75;state.qrBorder=4;state.qrPreviewCleared=false;state.ai=settings;state.texts.clear();state.texts.add(new MainActivity.TextItem());state.activeText=0;state.text="";a.rebuild();a.showPage(9);});
                MainActivity.State requested=a.copyOptions(a.state[9]);QrService.Region expected=QrService.position(original,PAYLOAD,38,20,75,4,false);
                Bitmap hint=QrService.code(PAYLOAD,expected.size,expected.margin,false);
                try {int[] pixels=new int[hint.getWidth()*hint.getHeight()];hint.getPixels(pixels,0,hint.getWidth(),0,0,hint.getWidth(),hint.getHeight());Result decoded=new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(new RGBLuminanceSource(hint.getWidth(),hint.getHeight(),pixels))));require("H".equals(String.valueOf(decoded.getResultMetadata().get(ResultMetadataType.ERROR_CORRECTION_LEVEL))),"Production hint is not ECC-H");Images.encode(hint,new File(dir,"production-hint-ecc-h.png"),0,new Files.Job());}finally{hint.recycle();}
                report.put("ecc","H");report.put("requested",new JSONObject().put("x_percent",20).put("y_percent",75).put("size_percent",38));report.put("settings",new JSONObject().put("edge",settings.edge).put("steps",settings.steps).put("cfg",settings.cfg).put("denoise",settings.strength).put("control_strength",settings.controlStrength).put("seed",settings.seed).put("auto_prompt",true));
                long generationStart=System.currentTimeMillis();log("AI_PRODUCTION_GENERATION_BEGIN");
                UiTests.ui(a,()->{require(a.qrTool.aiPanel!=null,"Production AI panel missing");a.qrTool.aiPanel.generate();require(a.aiController.running,"Production AI action did not start");});
                boolean navigationChecked=false;
                while(a.aiController.running){
                    Thread.sleep(500);
                    if(!navigationChecked&&a.aiController.diffusion!=null&&a.aiController.progress!=null&&a.aiController.progress.count==0){
                        long navigationStart=System.currentTimeMillis();UiTests.ui(a,()->a.showPage(7));UiTests.frames(a);long settingsMs=System.currentTimeMillis()-navigationStart;
                        require(a.aiController.running&&a.aiController.job!=null&&!a.aiController.job.cancelled,"Opening Settings cancelled native generation");navigationStart=System.currentTimeMillis();UiTests.ui(a,()->a.showPage(9));UiTests.frames(a);long qrMs=System.currentTimeMillis()-navigationStart;
                        require(a.aiController.running&&a.aiController.job!=null&&!a.aiController.job.cancelled,"Returning to QR cancelled native generation");require(settingsMs<=2000&&qrMs<=2000,"Navigation frozen during actual native work");report.put("settings_navigation_ms",settingsMs);report.put("qr_return_ms",qrMs);report.put("generation_running_during_navigation",true);navigationChecked=true;log("PASS NATIVE_NAVIGATION settings="+settingsMs+" ms qr="+qrMs+" ms");
                    }
                    if(System.currentTimeMillis()-generationStart>90*60*1000L){UiTests.ui(a,a.aiController::cancel);throw new IOException("Production AI exceeded test timeout");}
                }
                require(navigationChecked,"No live native work observed for navigation verification");
                UiTests.ui(a,()->{});report.put("generation_ms",System.currentTimeMillis()-generationStart);
                AiQrSettings result=a.state[9].ai;require(result.result!=null&&result.result.isFile(),"Production action returned no verified output: "+a.aiController.status);require(result.description!=null&&!result.description.trim().isEmpty(),"Automatic BLIP prompt missing");
                require(result.resultKey.equals(AndroidAiController.key(requested,settings)),"Generated result cache identity differs");require(a.state[9].qrX==20&&a.state[9].qrY==75&&a.state[9].qrSize==38,"Manual placement was changed");require(result.region.x==expected.x&&result.region.y==expected.y&&result.region.size==expected.size,"Result moved away from manually chosen region");
                Bitmap output=Images.load(result.result,0,true),analysis=null;
                try {
                    require(output.getWidth()==1920&&output.getHeight()==1280,"Generated canvas cropped or stretched");Files.Job job=new Files.Job();require(QrService.matches(output,PAYLOAD,job),"Full-canvas exact decode failed");require(QrArt.reducedMatches(output,PAYLOAD,job),"Reduced-canvas exact decode failed");
                    int changedInside=0;int[] before=new int[1920],after=new int[1920];for(int y=0;y<1280;y++){original.getPixels(before,0,1920,0,y,1920,1);output.getPixels(after,0,1920,0,y,1920,1);for(int x=0;x<1920;x++)if(before[x]!=after[x]){require(x>=expected.x&&x<expected.x+expected.size&&y>=expected.y&&y<expected.y+expected.size,"Original pixels changed outside requested QR region");changedInside++;}}
                    require(changedInside>0,"AI output contains no changed pixels");analysis=Images.fit(original,1600,1067,Color.WHITE);AiVision.MaterialMap map=AiVision.materials(analysis,store.root,job);int protectedCount=0;for(int y=0;y<1280;y++){original.getPixels(before,0,1920,0,y,1920,1);output.getPixels(after,0,1920,0,y,1920,1);for(int x=0;x<1920;x++){int mx=Math.min(map.width-1,x*map.width/1920),my=Math.min(map.height-1,y*map.height/1280),index=my*map.width+mx;if(map.isProtected(mx,my)&&(map.skinMask[index]==0||map.identityMask[index]!=0)){require(before[x]==after[x],"Protected face/hair pixels changed");protectedCount++;}}}
                    File copied=new File(dir,"production-complex-manual.png");try(InputStream in=new FileInputStream(result.result);OutputStream out=new FileOutputStream(copied)){Files.copy(in,out,job);}report.put("output",copied.getAbsolutePath());report.put("canvas",new org.json.JSONArray(Arrays.asList(1920,1280)));report.put("region",new JSONObject().put("x",expected.x).put("y",expected.y).put("size",expected.size));report.put("full_decode",true);report.put("reduced_decode",true);report.put("unchanged_outside_region",true);report.put("protected_pixels",protectedCount);report.put("changed_inside_pixels",changedInside);report.put("description_and_prompt",result.description);report.put("sha256",Files.hash(copied,job));
                } finally {output.recycle();if(analysis!=null)analysis.recycle();}
                report.put("pass",true);log("AI_PRODUCTION_COMPLETE PASS");
            } finally {original.recycle();}
        } catch(Throwable error){try{report.put("pass",false);report.put("error",error.toString());StringWriter stack=new StringWriter();error.printStackTrace(new PrintWriter(stack));report.put("stack",stack.toString());}catch(Exception ignored){}Log.e("AIPT-AI-Test","AI_PRODUCTION_COMPLETE FAIL",error);}
        finally {try{report.put("elapsed_ms",System.currentTimeMillis()-started);if(reportFile!=null)write(reportFile,report);}catch(Exception error){Log.e("AIPT-AI-Test","Saving report",error);}}
    }
    static void write(File file,JSONObject value)throws Exception{try(FileOutputStream out=new FileOutputStream(file)){out.write(value.toString(2).getBytes(Files.UTF8));out.getFD().sync();}}
}
