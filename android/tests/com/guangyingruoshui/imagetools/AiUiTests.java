package com.guangyingruoshui.imagetools;

import android.graphics.*;
import android.view.View;
import java.io.*;
import java.util.*;

final class AiUiTests {
    static void require(boolean condition,String text){if(!condition)throw new IllegalStateException(text);}
    static void cleanupProfiles(MainActivity a){
        android.content.SharedPreferences prefs=a.getSharedPreferences("ai-profile-cleanup-regression",0);
        prefs.edit().clear().commit();
        try{
            AiQrSettings settings=new AiQrSettings();settings.prompt="default blue buildings";settings.steps=24;settings.save(prefs);
            settings.base="custom-base.safetensors";settings.control="custom-control.safetensors";settings.profile(prefs);settings.prompt="custom flowering trees";settings.steps=37;settings.save(prefs);
            settings.resetSelections(prefs,true);
            require(settings.base.isEmpty()&&settings.prompt.equals("default blue buildings")&&settings.steps==24,"Deleting a custom base overwrites the default model profile");
            require(prefs.getString("androidAiProfile:sd15:custom-base.safetensors:prompt","").equals("custom flowering trees"),"Deleting a model loses its retained profile history");
            settings.selectFamily(prefs,"sdxl");settings.prompt="default XL city";settings.save(prefs);settings.base="deleted-xl.safetensors";settings.control="deleted-xl-control.safetensors";settings.profile(prefs);settings.prompt="custom XL scene";settings.save(prefs);
            settings.selectFamily(prefs,"sd15");settings.resetSelections(prefs,null);settings.selectFamily(prefs,"sdxl");
            require(settings.base.isEmpty()&&settings.control.isEmpty()&&settings.prompt.equals("default XL city"),"Clear all restores a deleted path or overwrites the other family default profile");
        }finally{prefs.edit().clear().commit();}
    }
    static void run(MainActivity a,File dir,Files.Job job,StringBuilder report)throws Exception {
        AiQrSettings defaults=new AiQrSettings();require(defaults.strength==.42f&&defaults.controlStrength==.8f,"Fresh mobile material defaults regressed");
        for(String family:new String[]{"sd15","sdxl"}){
            String background=AiVision.materialPrompt("a girl in a school uniform","pink trees in front of city buildings",family,false);
            require(background.contains("building facades")&&background.contains("branches")&&!background.contains("fabric")&&!background.contains("garment"),"Global clothing leaks into local scenery prompt: "+family);
            String clothing=AiVision.materialPrompt("a portrait in a room","a knitted floral sweater",family,false);
            require(clothing.contains("ribbed wool yarn")&&clothing.contains("floral fabric pattern")&&!clothing.contains("building facades"),"Local garment prompt loses its existing material: "+family);
            require(AiVision.materialPrompt("a portrait","an arm",family,true).contains("flesh tone"),"Manual skin material prompt missing: "+family);
        }
        File base=new File(dir,"ai-placement-base.png");Bitmap image=Bitmap.createBitmap(640,960,Bitmap.Config.ARGB_8888);image.eraseColor(0xffa4c6e8);Images.encode(image,base,0,job);image.recycle();
        AiQrSettings[] previous={null};File[] previousCover={null};int[] mode={0};
        UiTests.ui(a,()->{MainActivity.State state=a.state[9];previous[0]=state.ai;previousCover[0]=state.cover;mode[0]=state.qrMode;state.ai=new AiQrSettings();state.cover=base;state.qrMode=3;state.qrPreviewCleared=false;a.rebuild();a.showPage(9);});
        try {
            AiModelStore constructed=new AiModelStore(a);
            require(constructed.root.isDirectory()&&constructed.resources.size()>10,"Fresh Android model store constructor cannot load the pinned manifest or create app-owned storage");
            require(AiModelStore.safe(new File("/"),"base/default/model.safetensors").getCanonicalPath().equals("/base/default/model.safetensors"),"Filesystem-root model path boundary fails on Android");
            UiTests.ui(a,()->cleanupProfiles(a));
            MainActivity.State captured=a.copyOptions(a.state[9]);Bitmap placed=a.qrTool.generate(captured,false,job);
            try{require(placed.getWidth()==640&&placed.getHeight()==960&&QrService.matches(placed,QrService.content(captured.qrUrl,captured.qrRaw),job),"Manual pre-generation preview lacks its positioned QR or crops the canvas");}finally{placed.recycle();}
            captured.ai.autoPlace=true;Bitmap automatic=a.qrTool.generate(captured,false,job);
            try{require(automatic.getWidth()==640&&automatic.getHeight()==960&&automatic.getPixel(320,480)==0xffa4c6e8&&!QrService.matches(automatic,QrService.content(captured.qrUrl,captured.qrRaw),job),"Automatic pre-generation preview includes an ordinary QR");}finally{automatic.recycle();}
            UiTests.ui(a,()->{QrTool q=a.qrTool;QrAiPanel p=q.aiPanel;MainActivity.State state=q.s;
                require(p.autoPrompt.getVisibility()==View.VISIBLE&&p.manual.isChecked(),"Independent caption option hidden in manual mode");p.autoPrompt.performClick();require(!state.ai.autoPrompt&&p.prompt.isEnabled(),"Cannot edit manual prompt");p.prompt.setText("blue buildings and flowering trees");require(state.ai.prompt.equals("blue buildings and flowering trees"),"Typed prompt not captured");
                int x=state.qrX,y=state.qrY;p.autoPlace.performClick();require(state.ai.autoPlace&&!state.ai.manualPlace&&state.ai.autoPrompt,"Placement switches not at-most-one");require(q.positionPanel.getVisibility()==View.GONE&&!q.preview.freeText,"Automatic placement leaves misleading manual controls");
                p.autoPrompt.performClick();require(p.prompt.isEnabled(),"Automatic placement blocks independent manual prompt");p.manual.performClick();require(!state.ai.autoPlace&&state.ai.manualPlace&&state.ai.autoPrompt&&q.positionPanel.getVisibility()==View.VISIBLE,"Manual placement does not restore position controls");p.manual.performClick();require(!state.ai.autoPlace&&!state.ai.manualPlace&&state.ai.autoPrompt,"Neither placement mode unexpectedly changes captioning");p.autoPrompt.performClick();require(!state.ai.autoPrompt&&p.prompt.isEnabled()&&state.qrX==x&&state.qrY==y,"Turning off captioning changes manual position");
                String key=AndroidAiController.key(state,state.ai);state.ai.seed++;require(!key.equals(AndroidAiController.key(state,state.ai)),"Seed fails to invalidate generated result");state.ai.promptTemplate="material, {prompt}";state.ai.customTemplate=true;state.ai.clipSkip=2;state.ai.save(a.prefs);AiQrSettings restored=new AiQrSettings();restored.load(a.prefs);require(restored.promptTemplate.equals(state.ai.promptTemplate)&&restored.seed==state.ai.seed&&restored.clipSkip==2&&!restored.autoPrompt,"Per-model profile not persisted");
                q.clearPreview();require(state.cover==null&&q.preview.image==null&&state.qrPreviewCleared&&state.qrUrl.length()>0&&state.ai.customTemplate,"Clear preview loses content/models/profile");
            });
            byte[] labels=new byte[96*96];for(int y=20;y<48;y++)for(int x=20;x<48;x++)labels[y*96+x]=3;for(int y=52;y<90;y++)for(int x=50;x<90;x++)labels[y*96+x]=12;
            AiVision.MaterialMap map=new AiVision.MaterialMap(96,96,labels);Bitmap original=Bitmap.createBitmap(96,96,Bitmap.Config.ARGB_8888);original.eraseColor(0xffe1b99a);Bitmap changed=original.copy(Bitmap.Config.ARGB_8888,true);changed.eraseColor(Color.BLUE);QrService.Region region=new QrService.Region();region.x=region.y=0;region.size=96;
            try{AndroidAiController.restoreProtected(changed,original,map,region,false,job);require(changed.getPixel(32,32)==original.getPixel(32,32),"Manual placement changes a face");require(changed.getPixel(70,70)==Color.BLUE,"Manual skin placement never edits the chosen skin area");require(changed.getPixel(51,53)==original.getPixel(51,53),"Manual skin editing bleeds across the eroded boundary");AndroidAiController.restoreProtected(changed,original,map,region,true,job);require(changed.getPixel(70,70)==original.getPixel(70,70),"Automatic avoidance changes protected skin");}finally{original.recycle();changed.recycle();}
            Bitmap white=Bitmap.createBitmap(96,96,Bitmap.Config.ARGB_8888);white.eraseColor(Color.WHITE);new Canvas(white).drawRect(20,20,48,48,new Paint());Bitmap selection=AiBackground.mask(white,map,job);
            try{require(selection!=null&&selection.getPixel(0,0)==Color.WHITE&&selection.getPixel(32,32)==Color.BLACK,"Background detector includes a protected subject");}finally{white.recycle();if(selection!=null)selection.recycle();}
            require(new AiModelStore.Snapshot(100,100,0,1,""," ").percent==99,"Unverified download reports completion");
            report.append("PASS: actual AI checkbox and prompt edits, both placements off, hidden automatic-position controls, clear preview, profile persistence, deleting a custom model restores the default profile, clearing all removes selections across both families, result invalidation, face preservation/manual skin boundaries, blank-background protection and verified download completion gate\n");
        }finally{UiTests.ui(a,()->{MainActivity.State state=a.state[9];state.ai=previous[0];state.cover=previousCover[0];state.qrMode=mode[0];state.qrPreviewCleared=false;state.ai.save(a.prefs);a.rebuild();});}
    }
}
