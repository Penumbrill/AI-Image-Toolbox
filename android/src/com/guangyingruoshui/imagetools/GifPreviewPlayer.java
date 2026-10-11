package com.guangyingruoshui.imagetools;

import android.graphics.Bitmap;
import android.widget.Button;
import android.widget.TextView;
import java.io.File;
import java.util.ArrayList;

/** Loads one preview frame at a time; stops when its page or activity is hidden. */
final class GifPreviewPlayer {
    final MainActivity a;final MainActivity.State state;final Widgets.Preview preview;final TextView status;final Button start,stop;
    final ArrayList<File> frames=new ArrayList<>();boolean running;int generation,next,presented,lastFrame=-1;
    final Runnable tick=this::load;
    GifPreviewPlayer(MainActivity activity,MainActivity.State s,Widgets.Preview v,TextView info,Button play,Button pause){a=activity;state=s;preview=v;status=info;start=play;stop=pause;v.hidden=this::stop;stop.setEnabled(false);}
    void start(){stop();if(state.entries.size()<2){a.toast(a.t("请选择至少两张图片","Choose at least two images"));return;}for(MainActivity.Entry e:a.snapshot(state,true))frames.add(e.file);if(frames.size()<2){a.toast(a.t("请至少勾选两张图片","Check at least two images"));frames.clear();return;}running=true;next=0;presented=0;lastFrame=-1;generation++;preview.ticket++;start.setEnabled(false);stop.setEnabled(true);load();}
    void stop(){generation++;running=false;a.ui.removeCallbacks(tick);frames.clear();if(start!=null)start.setEnabled(true);if(stop!=null)stop.setEnabled(false);}
    void load(){if(!running)return;if(a.destroyed||a.busy||a.page!=3||!preview.isShown()||preview.getWindowVisibility()!=android.view.View.VISIBLE){stop();return;}if(next>=frames.size()){if(!state.gifLoop){stop();status.setText(a.t("播放结束","Playback finished"));return;}next=0;}final int index=next++,request=generation;final File frame=frames.get(index);a.worker.execute(()->{Bitmap loaded=null;String failure=null;try{loaded=Images.load(frame,640,false);}catch(Throwable problem){failure=a.error(problem);}final Bitmap image=loaded;final String error=failure;a.ui.post(()->{if(!running||request!=generation||a.destroyed||a.page!=3||!preview.isShown()||preview.getWindowVisibility()!=android.view.View.VISIBLE){if(image!=null)image.recycle();return;}if(image==null){stop();status.setText(error);return;}preview.set(image);presented++;lastFrame=index;status.setText(a.t("播放帧 ","Playing frame ")+(index+1)+" / "+frames.size());a.ui.postDelayed(tick,Math.max(20,state.gifDelay));});});}
}
