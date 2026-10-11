package com.guangyingruoshui.imagetools;

import android.app.AlertDialog;
import android.graphics.Color;
import android.text.*;
import android.view.View;
import android.widget.*;
import java.util.Locale;

/** RGBA controls and an exact hexadecimal value; no third-party picker dependency. */
final class ColorPicker {
    interface Changed {void color(int value);}
    static int parse(String value){String s=value.trim();if(s.startsWith("#"))s=s.substring(1);if(!s.matches("[a-fA-F0-9]{6}|[a-fA-F0-9]{8}"))throw new IllegalArgumentException("Use #RRGGBB or #AARRGGBB");long color=Long.parseLong(s,16);return (int)(s.length()==6?color|0xff000000L:color);}
    static String hex(int value){return String.format(Locale.ROOT,"#%08X",value);}
    static AlertDialog show(MainActivity a,String title,int initial,Changed changed){
        LinearLayout body=a.column();body.setPadding(a.dp(16),a.dp(12),a.dp(16),a.dp(8));TextView sample=a.label("",16,true);sample.setTag("color-sample");sample.setMinHeight(a.dp(52));body.addView(sample);
        EditText text=new EditText(a);text.setTag("color-value");text.setSingleLine(true);text.setHint("#RRGGBB / #AARRGGBB");body.addView(text);body.addView(a.label(a.t("可输入任意颜色；8 位包含透明度，6 位为不透明。","Enter any color; 8 digits include opacity, 6 digits are opaque."),12,false));
        int[] color={initial};boolean[] updating={false};SeekBar[] bars=new SeekBar[4];int[] shifts={16,8,0,24};String[] names={a.t("红","Red"),a.t("绿","Green"),a.t("蓝","Blue"),a.t("不透明度","Opacity")};
        Runnable refresh=()->{updating[0]=true;try{text.setText(hex(color[0]));sample.setText(hex(color[0]));sample.setBackgroundColor(color[0]);sample.setTextColor(Color.red(color[0])+Color.green(color[0])+Color.blue(color[0])>400?Color.BLACK:Color.WHITE);for(int i=0;i<bars.length;i++)if(bars[i]!=null)bars[i].setProgress(color[0]>>>shifts[i]&255);}finally{updating[0]=false;}};
        for(int i=0;i<4;i++){final int shift=shifts[i];bars[i]=a.slider(body,names[i],0,255,initial>>>shift&255,v->{color[0]=color[0]&~(255<<shift)|v<<shift;refresh.run();});}
        text.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int start,int count,int after){}public void onTextChanged(CharSequence s,int start,int before,int count){if(updating[0])return;try{color[0]=parse(s.toString());sample.setText(hex(color[0]));sample.setBackgroundColor(color[0]);text.setError(null);updating[0]=true;for(int i=0;i<bars.length;i++)bars[i].setProgress(color[0]>>>shifts[i]&255);updating[0]=false;}catch(IllegalArgumentException ignored){text.setError(a.t("请输入 6 或 8 位十六进制颜色","Enter a 6- or 8-digit hexadecimal color"));}}public void afterTextChanged(Editable value){}});
        refresh.run();ScrollView scroll=new ScrollView(a);scroll.addView(body);AlertDialog dialog=new AlertDialog.Builder(a).setTitle(title).setView(scroll).setPositiveButton(a.t("应用","Apply"),null).setNegativeButton(a.t("取消","Cancel"),null).create();dialog.setOnShowListener(d->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v->{try{int selected=parse(text.getText().toString());changed.color(selected);dialog.dismiss();}catch(IllegalArgumentException bad){text.setError(a.t("请输入 6 或 8 位十六进制颜色","Enter a 6- or 8-digit hexadecimal color"));}}));dialog.show();return dialog;
    }
}
