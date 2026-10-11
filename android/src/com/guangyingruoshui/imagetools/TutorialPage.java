package com.guangyingruoshui.imagetools;

import android.widget.*;
import android.os.Build;
import org.json.*;
import java.io.*;
import java.util.*;

final class TutorialPage {
    static void build(MainActivity a,LinearLayout body){
        try{
            ByteArrayOutputStream bytes=new ByteArrayOutputStream();
            try(InputStream input=a.getAssets().open(a.english?"tutorial.en.json":"tutorial.zh.json")){byte[] buffer=new byte[8192];int count;while((count=input.read(buffer))!=-1)bytes.write(buffer,0,count);}
            JSONObject data=new JSONObject(new String(bytes.toByteArray(),"UTF-8"));JSONArray chapters=data.getJSONArray("chapters");
            body.addView(a.label(data.getString("description"),14,false));LinearLayout toc=a.card(body,data.getString("contents"));
            ArrayList<LinearLayout> anchors=new ArrayList<>();
            for(int i=0;i<chapters.length();i++){
                final int index=i;Button link=a.button((i+1)+". "+chapters.getJSONObject(i).getString("title"),()->{if(index<anchors.size())a.pages[10].post(()->a.pages[10].smoothScrollTo(0,anchors.get(index).getTop()));});
                link.setGravity(android.view.Gravity.CENTER_VERTICAL|android.view.Gravity.START);link.setSingleLine(false);toc.addView(link,new LinearLayout.LayoutParams(-1,-2));
            }
            for(int i=0;i<chapters.length();i++){
                JSONObject chapter=chapters.getJSONObject(i);LinearLayout card=a.card(body,(i+1)+". "+chapter.getString("title"));anchors.add(card);
                paragraph(a,card,chapter.getString("intro"));heading(a,card,chapter.getString("stepsTitle"));JSONArray steps=chapter.getJSONArray("steps");for(int j=0;j<steps.length();j++)paragraph(a,card,(j+1)+". "+steps.getString(j));
                heading(a,card,chapter.getString("optionsTitle"));JSONArray options=chapter.getJSONArray("options");for(int j=0;j<options.length();j++)paragraph(a,card,"• "+options.getString(j));
                heading(a,card,chapter.getString("outputTitle"));paragraph(a,card,chapter.getString("output"));a.buttons(card,a.button(data.getString("back"),()->a.pages[10].smoothScrollTo(0,0)));
            }
        }catch(Exception e){body.addView(a.label(a.t("教程无法加载，请查看 README。","Unable to load the guide. See the README."),14,false));}
    }
    static void paragraph(MainActivity a,LinearLayout card,String text){TextView label=a.label(text,14,false);label.setTextIsSelectable(true);label.setLineSpacing(a.dp(3),1);card.addView(label);}
    static void heading(MainActivity a,LinearLayout card,String text){TextView label=a.label(text,16,true);if(Build.VERSION.SDK_INT>=28)label.setAccessibilityHeading(true);card.addView(label);}
}
