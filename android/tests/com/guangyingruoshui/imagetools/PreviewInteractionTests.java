package com.guangyingruoshui.imagetools;

import android.graphics.Bitmap;
import android.os.SystemClock;
import android.view.MotionEvent;
import android.widget.FrameLayout;

/** Device tests: dispatch touch events through the production preview widget. */
final class PreviewInteractionTests {
    static void require(boolean value,String label){if(!value)throw new IllegalStateException(label);}
    static void touch(Widgets.Preview view,long down,int action,float x,float y){
        MotionEvent event=MotionEvent.obtain(down,SystemClock.uptimeMillis(),action,x,y,0);
        try{view.dispatchTouchEvent(event);}finally{event.recycle();}
    }
    static void run(MainActivity activity,StringBuilder report)throws Exception{
        Widgets.Preview[] view={null};int[] clicks={0},moves={0},finished={0};
        UiTests.ui(activity,()->{
            Widgets.Preview preview=new Widgets.Preview(activity,"Preview interaction test");
            preview.freeText=true;preview.move=(x,y)->moves[0]++;
            preview.dragFinished=()->finished[0]++;
            preview.setOnClickListener(v->clicks[0]++);
            activity.root.addView(preview,new FrameLayout.LayoutParams(activity.dp(240),activity.dp(200)));
            view[0]=preview;
        });
        try{
            UiTests.frames(activity);Widgets.Preview preview=view[0];
            long emptyDown=SystemClock.uptimeMillis();
            UiTests.ui(activity,()->{touch(preview,emptyDown,MotionEvent.ACTION_DOWN,30,30);touch(preview,emptyDown,MotionEvent.ACTION_UP,30,30);});
            UiTests.frames(activity);
            UiTests.ui(activity,()->{require(clicks[0]==1,"Empty placement preview did not click exactly once");require(moves[0]==0&&!preview.dragging,"Empty preview started placement drag");preview.set(Bitmap.createBitmap(64,64,Bitmap.Config.ARGB_8888));});
            long dragDown=SystemClock.uptimeMillis();
            UiTests.ui(activity,()->touch(preview,dragDown,MotionEvent.ACTION_DOWN,30,30));
            Thread.sleep(450);
            UiTests.ui(activity,()->{
                require(preview.dragging,"Held preview did not enter placement drag");
                touch(preview,dragDown,MotionEvent.ACTION_MOVE,90,90);
                touch(preview,dragDown,MotionEvent.ACTION_UP,90,90);
                require(moves[0]>0&&finished[0]==1,"Placement drag did not deliver final position");
                require(clicks[0]==1,"Placement drag incorrectly triggered picker click");
                require(!preview.dragging&&!preview.movePending,"Placement drag left pending state");
            });
            long scrollDown=SystemClock.uptimeMillis();
            UiTests.ui(activity,()->{
                touch(preview,scrollDown,MotionEvent.ACTION_DOWN,30,30);
                touch(preview,scrollDown,MotionEvent.ACTION_MOVE,120,120);
                touch(preview,scrollDown,MotionEvent.ACTION_UP,120,120);
                require(clicks[0]==1,"Moving before hold incorrectly clicked preview");
            });
            long cancelDown=SystemClock.uptimeMillis();
            UiTests.ui(activity,()->{
                touch(preview,cancelDown,MotionEvent.ACTION_DOWN,30,30);
                touch(preview,cancelDown,MotionEvent.ACTION_CANCEL,30,30);
            });
            Thread.sleep(450);
            UiTests.ui(activity,()->{require(!preview.dragging&&clicks[0]==1,"Cancelled touch left a hold or click");preview.set(null);});
            long clearedDown=SystemClock.uptimeMillis();
            UiTests.ui(activity,()->{touch(preview,clearedDown,MotionEvent.ACTION_DOWN,30,30);touch(preview,clearedDown,MotionEvent.ACTION_UP,30,30);});
            UiTests.frames(activity);
            UiTests.ui(activity,()->{require(clicks[0]==2,"Cleared preview no longer accepts click");preview.freeText=false;preview.set(Bitmap.createBitmap(64,64,Bitmap.Config.ARGB_8888));});
            long regularDown=SystemClock.uptimeMillis();
            UiTests.ui(activity,()->{touch(preview,regularDown,MotionEvent.ACTION_DOWN,30,30);touch(preview,regularDown,MotionEvent.ACTION_UP,30,30);});
            UiTests.frames(activity);
            UiTests.ui(activity,()->{
                require(clicks[0]==3,"Ordinary image preview click was intercepted");
                int[] imports={0};boolean previousBusy=activity.busy;
                try{
                    activity.bindEmptyPreview(preview,()->imports[0]++);
                    preview.performClick();require(imports[0]==0,"Import binding reopened loaded preview");
                    preview.set(null);preview.performClick();require(imports[0]==1,"Empty preview import binding has no action");
                    activity.busy=true;preview.performClick();require(imports[0]==1,"Empty preview imported during processing");
                }finally{activity.busy=previousBusy;}
            });
            report.append("PASS: real preview touch events click empty/cleared/ordinary views; placement drag, pre-hold movement and cancellation do not invoke picker click; production empty-import binding ignores loaded views and busy processing\n");
        }finally{
            UiTests.ui(activity,()->{Widgets.Preview preview=view[0];if(preview!=null){if(preview.press!=null)preview.removeCallbacks(preview.press);preview.removeCallbacks(preview.deliverMove);activity.root.removeView(preview);preview.set(null);}});
        }
    }
}
