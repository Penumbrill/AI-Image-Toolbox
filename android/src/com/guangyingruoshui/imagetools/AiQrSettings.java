package com.guangyingruoshui.imagetools;

import android.content.SharedPreferences;
import java.io.File;

/** Placement and captioning are separate choices; only the two placement modes exclude each other. */
final class AiQrSettings {
    boolean autoPlace=false,manualPlace=true,autoPrompt=true,background=false,customTemplate=false;
    String family="sd15",source="auto",base="",control="",prompt="",negative=AiVision.NEGATIVE;
    String promptTemplate="{prompt}",negativeTemplate="{negative}";
    int steps=24,edge=0,threads=4,seed=17371,clipSkip=0;float cfg=5.5f,strength=.42f,controlStrength=.8f;
    File result;String resultKey="",description="";QrService.Region region;
    AiQrSettings copy(){AiQrSettings n=new AiQrSettings();n.autoPlace=autoPlace;n.manualPlace=manualPlace;n.autoPrompt=autoPrompt;n.background=background;n.customTemplate=customTemplate;n.promptTemplate=promptTemplate;n.negativeTemplate=negativeTemplate;n.family=family;n.source=source;n.base=base;n.control=control;n.prompt=prompt;n.negative=negative;n.steps=steps;n.edge=edge;n.threads=threads;n.seed=seed;n.clipSkip=clipSkip;n.cfg=cfg;n.strength=strength;n.controlStrength=controlStrength;n.result=result;n.resultKey=resultKey;n.description=description;n.region=region;return n;}
    void placement(boolean automatic,boolean checked){if(automatic){autoPlace=checked;if(checked)manualPlace=false;}else{manualPlace=checked;if(checked)autoPlace=false;}if(checked)autoPrompt=true;}
    void load(SharedPreferences p){family=p.getString("androidAiFamily","sd15");source=p.getString("androidAiSource","auto");autoPlace=p.getBoolean("androidAiAutoPlace",false);manualPlace=!autoPlace&&p.getBoolean("androidAiManualPlace",true);autoPrompt=p.getBoolean("androidAiAutoPrompt",true);background=p.getBoolean("androidAiBackground",false);base=p.getString("androidAiCustomBase","");control=p.getString("androidAiCustomControl","");profile(p);}
    String profileKey(){return "androidAiProfile:"+family+":"+base;}
    void selectFamily(SharedPreferences p,String selected){save(p);family=selected;base=p.getString("androidAiSelection:"+family+":base","");control=p.getString("androidAiSelection:"+family+":control","");profile(p);save(p);}
    void resetSelections(SharedPreferences p,Boolean deletedBase){
        save(p);
        if(deletedBase==null){
            SharedPreferences.Editor edit=p.edit();
            for(String type:new String[]{"sd15","sdxl"})edit.remove("androidAiSelection:"+type+":base").remove("androidAiSelection:"+type+":control");
            edit.apply();base="";control="";profile(p);
        }else if(deletedBase){base="";profile(p);}else control="";
        clearResult();save(p);
    }
    static float clamp(float v,float low,float high,float fallback){return Float.isNaN(v)||Float.isInfinite(v)?fallback:Math.max(low,Math.min(high,v));}
    void profile(SharedPreferences p){String key=profileKey();prompt=p.getString(key+":prompt","");negative=p.getString(key+":negative",AiVision.NEGATIVE);customTemplate=p.getBoolean(key+":custom",false);promptTemplate=p.getString(key+":template","{prompt}");negativeTemplate=p.getString(key+":negativeTemplate","{negative}");steps=Math.max(8,Math.min(60,p.getInt(key+":steps",family.equals("sdxl")?30:24)));cfg=clamp(p.getFloat(key+":cfg",5.5f),1,12,5.5f);strength=clamp(p.getFloat(key+":strength",.42f),.15f,.85f,.42f);controlStrength=clamp(p.getFloat(key+":control",.8f),.3f,2.5f,.8f);edge=p.getInt(key+":edge",0);if(edge!=320&&edge!=384&&edge!=512&&edge!=768)edge=0;threads=Math.max(1,Math.min(8,p.getInt(key+":threads",4)));seed=p.getInt(key+":seed",17371);clipSkip=family.equals("sdxl")?0:Math.max(0,Math.min(2,p.getInt(key+":clip",0)));}
    void save(SharedPreferences p){String key=profileKey();p.edit().putString("androidAiSelection:"+family+":base",base).putString("androidAiSelection:"+family+":control",control).putString("androidAiFamily",family).putString("androidAiSource",source).putBoolean("androidAiAutoPlace",autoPlace).putBoolean("androidAiManualPlace",manualPlace).putBoolean("androidAiAutoPrompt",autoPrompt).putBoolean("androidAiBackground",background).putString("androidAiCustomBase",base).putString("androidAiCustomControl",control).putString(key+":prompt",prompt).putString(key+":negative",negative).putBoolean(key+":custom",customTemplate).putString(key+":template",promptTemplate).putString(key+":negativeTemplate",negativeTemplate).putInt(key+":steps",steps).putInt(key+":edge",edge).putInt(key+":threads",threads).putInt(key+":seed",seed).putInt(key+":clip",clipSkip).putFloat(key+":cfg",cfg).putFloat(key+":strength",strength).putFloat(key+":control",controlStrength).apply();}
    static void validateTemplate(String value,String token)throws java.io.IOException {if(value==null||value.length()>1800||!value.contains(token)||value.replace(token,"").contains("{")||value.replace(token,"").contains("}"))throw new java.io.IOException("自定义模板必须保留 "+token+" / Custom template must preserve "+token);}
    void clearResult(){if(result!=null)result.delete();result=null;resultKey="";region=null;description="";}
}
