package com.guangyingruoshui.imagetools;

import android.content.Context;
import android.graphics.*;
import android.view.*;
import android.widget.*;
import java.util.*;

final class Widgets {
    static int dp(Context c,float v){return Math.round(v*c.getResources().getDisplayMetrics().density);}
    static class Flow extends ViewGroup {int gap;Flow(Context c){super(c);gap=dp(c,8);}protected void onMeasure(int ws,int hs){int width=MeasureSpec.getSize(ws),x=0,y=0,row=0;for(int i=0;i<getChildCount();i++){View v=getChildAt(i);if(v.getVisibility()==GONE)continue;v.measure(MeasureSpec.makeMeasureSpec(Math.max(1,width),MeasureSpec.AT_MOST),MeasureSpec.makeMeasureSpec(0,MeasureSpec.UNSPECIFIED));if(x>0&&x+v.getMeasuredWidth()>width){y+=row+gap;x=0;row=0;}x+=v.getMeasuredWidth()+gap;row=Math.max(row,v.getMeasuredHeight());}setMeasuredDimension(width,y+row);}protected void onLayout(boolean changed,int l,int t,int r,int b){int width=r-l,x=0,y=0,row=0,start=0;for(int i=0;i<=getChildCount();i++){View v=i<getChildCount()?getChildAt(i):null;if(v!=null&&v.getVisibility()==GONE)continue;if(v==null||x>0&&x+v.getMeasuredWidth()>width){int px=0;for(int k=start;k<i;k++){View child=getChildAt(k);if(child.getVisibility()==GONE)continue;int cy=y+(row-child.getMeasuredHeight())/2;child.layout(px,cy,px+child.getMeasuredWidth(),cy+child.getMeasuredHeight());px+=child.getMeasuredWidth()+gap;}y+=row+gap;x=0;row=0;start=i;}if(v!=null){x+=v.getMeasuredWidth()+gap;row=Math.max(row,v.getMeasuredHeight());}}}}
    static class PageScroll extends ScrollView {
        Runnable moving;
        PageScroll(Context c){super(c);}
        void moved(){if(moving!=null)moving.run();}
        protected void onScrollChanged(int l,int t,int oldl,int oldt){super.onScrollChanged(l,t,oldl,oldt);if(l!=oldl||t!=oldt)moved();}
        public boolean onInterceptTouchEvent(MotionEvent e){boolean intercept=super.onInterceptTouchEvent(e);if(intercept&&e.getActionMasked()==MotionEvent.ACTION_MOVE)moved();return intercept;}
        public boolean onTouchEvent(MotionEvent e){moved();return super.onTouchEvent(e);}
    }
    /** Three complete rows; native nested scrolling passes edge overflow to the page. */
    static class QueueList extends ListView {
        int rowHeight;final Rect window=new Rect();
        QueueList(Context c){super(c);setNestedScrollingEnabled(true);setChoiceMode(CHOICE_MODE_SINGLE);setPadding(0,0,0,0);setDividerHeight(dp(c,1));setVerticalScrollBarEnabled(true);setScrollbarFadingEnabled(false);setScrollBarStyle(SCROLLBARS_INSIDE_INSET);setOverScrollMode(OVER_SCROLL_NEVER);}
        int usableHeight(){getWindowVisibleDisplayFrame(window);return window.height()>0?window.height():getResources().getDisplayMetrics().heightPixels;}
        protected void onMeasure(int ws,int hs){int screen=usableHeight(),divider=getDividerHeight();int target=Math.round(screen*.29f);int next=Math.max(1,(target-2*divider)/3);if(rowHeight!=next){rowHeight=next;if(getAdapter() instanceof BaseAdapter)((BaseAdapter)getAdapter()).notifyDataSetChanged();}super.onMeasure(ws,MeasureSpec.makeMeasureSpec(rowHeight*3+divider*2,MeasureSpec.EXACTLY));}
        public boolean onTouchEvent(MotionEvent event){int action=event.getActionMasked();if(action==MotionEvent.ACTION_DOWN&&getParent()!=null)getParent().requestDisallowInterceptTouchEvent(true);try{return super.onTouchEvent(event);}finally{if((action==MotionEvent.ACTION_UP||action==MotionEvent.ACTION_CANCEL)&&getParent()!=null)getParent().requestDisallowInterceptTouchEvent(false);}}
    }
    /** A short position menu with three visible choices and a scrollable list. */
    static class PositionSpinner extends Spinner {
        ListPopupWindow popup;
        final int[] anchorPosition=new int[2],currentPosition=new int[2];
        ViewTreeObserver observedTree;
        final ViewTreeObserver.OnScrollChangedListener anchorScrolled=()->{if(popup!=null&&popup.isShowing()){getLocationOnScreen(currentPosition);if(currentPosition[0]!=anchorPosition[0]||currentPosition[1]!=anchorPosition[1]||!isShown())popup.dismiss();}};
        PositionSpinner(Context c){super(c,null,android.R.attr.spinnerStyle,Spinner.MODE_DIALOG);}
        public boolean performClick(){
            if(!isEnabled()||!isShown()||getAdapter()==null||getAdapter().getCount()==0)return false;
            if(popup!=null&&popup.isShowing())return true;
            popup=new ListPopupWindow(getContext());popup.setAnchorView(this);popup.setAdapter((ListAdapter)getAdapter());
            popup.setModal(true);popup.setWidth(getWidth());Rect padding=new Rect();if(popup.getBackground()!=null)popup.getBackground().getPadding(padding);popup.setHeight(dp(getContext(),48)*Math.min(3,getAdapter().getCount())+padding.top+padding.bottom);
            popup.setInputMethodMode(PopupWindow.INPUT_METHOD_NOT_NEEDED);
            popup.setOnItemClickListener((parent,view,position,id)->{popup.dismiss();setSelection(position);});
            getLocationOnScreen(anchorPosition);popup.show();ListView list=popup.getListView();list.setDividerHeight(0);list.setVerticalScrollBarEnabled(true);list.setScrollbarFadingEnabled(false);list.setSelection(getSelectedItemPosition());
            return true;
        }
        protected void onAttachedToWindow(){super.onAttachedToWindow();observedTree=getViewTreeObserver();observedTree.addOnScrollChangedListener(anchorScrolled);}
        protected void onVisibilityChanged(View changed,int visibility){super.onVisibilityChanged(changed,visibility);if(popup!=null&&!isShown())popup.dismiss();}
        public void setEnabled(boolean enabled){super.setEnabled(enabled);if(!enabled&&popup!=null)popup.dismiss();}
        protected void onDetachedFromWindow(){if(popup!=null)popup.dismiss();if(observedTree!=null&&observedTree.isAlive())observedTree.removeOnScrollChangedListener(anchorScrolled);observedTree=null;super.onDetachedFromWindow();}
    }
    static class Preview extends View {Bitmap image;Paint paint=new Paint(3);String empty;boolean freeText;int ticket;Runnable pending,press,reload,dragFinished,hidden;boolean previewQueued;final Runnable dispatchPreview=()->{previewQueued=false;Runnable latest=this.pending;this.pending=null;if(latest!=null)latest.run();};boolean dragging,movePending,touchMoved;final Runnable deliverMove=()->{movePending=false;if(dragging&&this.move!=null)this.move.moved(this.tx,this.ty);};float downX,downY,tx=50,ty=50;interface Move {void moved(float x,float y);}Move move;Preview(Context c,String label){super(c);empty=label;setContentDescription(label);}void set(Bitmap b){Bitmap old=image;image=b;if(old!=null&&old!=b&&!old.isRecycled())old.recycle();invalidate();}protected void onMeasure(int w,int h){int width=MeasureSpec.getSize(w);setMeasuredDimension(width,Math.min(dp(getContext(),270),Math.max(dp(getContext(),160),(int)(width*.62f))));}protected void onDraw(Canvas c){super.onDraw(c);c.drawColor(0xfff1eff8);if(image!=null&&!image.isRecycled()){float s=Math.min(getWidth()/(float)image.getWidth(),getHeight()/(float)image.getHeight());float w=image.getWidth()*s,h=image.getHeight()*s;c.drawBitmap(image,null,new RectF((getWidth()-w)/2,(getHeight()-h)/2,(getWidth()+w)/2,(getHeight()+h)/2),paint);}else{paint.setColor(0xff827793);paint.setTextSize(dp(getContext(),14));paint.setTextAlign(Paint.Align.CENTER);String[] lines=empty.split("\n",-1);float lineHeight=paint.getFontSpacing(),baseline=getHeight()/2f-(lines.length-1)*lineHeight/2f;for(int i=0;i<lines.length;i++)c.drawText(lines[i],getWidth()/2f,baseline+i*lineHeight,paint);}}protected void onVisibilityChanged(View changed,int visibility){super.onVisibilityChanged(changed,visibility);if(hidden!=null&&!isShown())hidden.run();}protected void onWindowVisibilityChanged(int visibility){super.onWindowVisibilityChanged(visibility);if(hidden!=null&&visibility!=View.VISIBLE)hidden.run();}protected void onDetachedFromWindow(){if(hidden!=null)hidden.run();super.onDetachedFromWindow();}public boolean onTouchEvent(MotionEvent e){
            // Empty previews use the ordinary View click path even in placement mode.
            if(image==null||image.isRecycled()||!freeText||move==null||!isEnabled())return super.onTouchEvent(e);
            int action=e.getActionMasked();
            if(action==MotionEvent.ACTION_DOWN){
                downX=e.getX();downY=e.getY();dragging=false;touchMoved=false;
                if(press!=null)removeCallbacks(press);
                press=()->{dragging=true;if(getParent()!=null)getParent().requestDisallowInterceptTouchEvent(true);performHapticFeedback(HapticFeedbackConstants.LONG_PRESS);};
                postDelayed(press,400);return true;
            }
            if(action==MotionEvent.ACTION_MOVE){
                if(!dragging){
                    if(Math.abs(e.getX()-downX)+Math.abs(e.getY()-downY)>dp(getContext(),8)){touchMoved=true;if(press!=null)removeCallbacks(press);}
                    return true;
                }
                float scale=Math.min(getWidth()/(float)image.getWidth(),getHeight()/(float)image.getHeight());
                float iw=image.getWidth()*scale,ih=image.getHeight()*scale;
                tx=Math.max(0,Math.min(100,(e.getX()-(getWidth()-iw)/2)/iw*100));
                ty=Math.max(0,Math.min(100,(e.getY()-(getHeight()-ih)/2)/ih*100));
                if(!movePending){movePending=true;postDelayed(deliverMove,33);}
            }
            if(action==MotionEvent.ACTION_UP||action==MotionEvent.ACTION_CANCEL){
                if(press!=null)removeCallbacks(press);
                boolean wasDragging=dragging;
                if(movePending){removeCallbacks(deliverMove);movePending=false;if(wasDragging&&move!=null)move.moved(tx,ty);}
                dragging=false;if(getParent()!=null)getParent().requestDisallowInterceptTouchEvent(false);
                if(wasDragging&&dragFinished!=null)dragFinished.run();
                // Ending a placement drag or a scrolling gesture must not open the picker.
                if(action==MotionEvent.ACTION_UP&&!wasDragging&&!touchMoved)performClick();
            }
            return true;
        }public boolean performClick(){super.performClick();return true;}}
    static class Editor extends View {Bitmap image,mask;final BitmapHistory history;Canvas maskCanvas;Paint paint=new Paint(3),stroke=new Paint(3),circle=new Paint(3);float zoom=1,panX,panY,px,py,startX,startY,lastX,lastY;int brush=36,strength=16,effect=0,coverColor=Color.BLACK;boolean rectangle=false,drawing=false,panning=false;Path path=new Path();RectF rect=new RectF();ScaleGestureDetector detector;Editor(Context c){super(c);history=new BitmapHistory(c);stroke.setColor(Color.WHITE);stroke.setStyle(Paint.Style.STROKE);stroke.setStrokeCap(Paint.Cap.ROUND);stroke.setStrokeJoin(Paint.Join.ROUND);circle.setStyle(Paint.Style.STROKE);circle.setStrokeWidth(dp(c,1));setLayerType(View.LAYER_TYPE_HARDWARE,null);detector=new ScaleGestureDetector(c,new ScaleGestureDetector.SimpleOnScaleGestureListener(){public boolean onScale(ScaleGestureDetector d){zoom=Math.max(.25f,Math.min(12,zoom*d.getScaleFactor()));drawing=false;invalidate();return true;}});setContentDescription("双指缩放和平移；单指涂抹 / Pinch and pan; paint with one finger");}
        long paintAfter;boolean unlockScheduled;Runnable readinessChanged;
        final Runnable unlockCheck=new Runnable(){public void run(){unlockScheduled=false;long remaining=paintAfter-android.os.SystemClock.uptimeMillis();if(remaining>0){unlockScheduled=true;postDelayed(this,remaining);}else if(readinessChanged!=null)readinessChanged.run();}};
        boolean paintReady(){return android.os.SystemClock.uptimeMillis()>=paintAfter;}
        void pausePainting(){boolean ready=paintReady(),active=drawing;paintAfter=android.os.SystemClock.uptimeMillis()+2000;drawing=false;panning=false;if(active){if(getParent()!=null)getParent().requestDisallowInterceptTouchEvent(false);invalidate();}if(ready&&readinessChanged!=null)readinessChanged.run();if(!unlockScheduled){unlockScheduled=true;postDelayed(unlockCheck,2000);}}
        protected void onAttachedToWindow(){super.onAttachedToWindow();pausePainting();}
        protected void onDetachedFromWindow(){removeCallbacks(unlockCheck);unlockScheduled=false;super.onDetachedFromWindow();}
        void set(Bitmap b){release();image=b;if(b!=null){mask=Bitmap.createBitmap(b.getWidth(),b.getHeight(),Bitmap.Config.ARGB_8888);maskCanvas=new Canvas(mask);}resetView();pausePainting();}void release(){if(image!=null)image.recycle();history.clear();if(mask!=null)mask.recycle();image=mask=null;}void resetView(){zoom=1;panX=panY=0;invalidate();}float scale(){return image==null?1:Math.min(getWidth()/(float)image.getWidth(),getHeight()/(float)image.getHeight())*zoom;}float left(){return image==null?0:(getWidth()-image.getWidth()*scale())/2+panX;}float top(){return image==null?0:(getHeight()-image.getHeight()*scale())/2+panY;}void clear(){if(mask!=null)mask.eraseColor(Color.TRANSPARENT);path.reset();rect.setEmpty();invalidate();}
        protected void onMeasure(int w,int h){int width=MeasureSpec.getSize(w),screen=getResources().getDisplayMetrics().heightPixels;setMeasuredDimension(width,Math.max(dp(getContext(),260),Math.min(dp(getContext(),560),(int)(screen*.5f))));}
        protected void onDraw(Canvas c){c.drawColor(0xffeeebf6);if(image==null)return;float s=scale();int save=c.save();c.translate(left(),top());c.scale(s,s);c.drawBitmap(image,0,0,paint);if(mask!=null){Paint selected=new Paint(3);selected.setColorFilter(new PorterDuffColorFilter(0x887c5dc6,PorterDuff.Mode.SRC_IN));c.drawBitmap(mask,0,0,selected);}if(drawing&&rectangle){circle.setColor(0xff7f65c5);circle.setStrokeWidth(1/s);c.drawRect(rect,circle);}c.restoreToCount(save);if(drawing&&!rectangle){circle.setStrokeWidth(dp(getContext(),1.2f));circle.setColor(Color.WHITE);c.drawCircle(px,py,brush*s/2,circle);circle.setColor(0xff6b529f);c.drawCircle(px,py,brush*s/2+dp(getContext(),1),circle);int cells=Math.min(12,Math.max(2,brush/Math.max(1,strength)));float step=brush*s/cells;int clip=c.save();c.clipPath(circlePath(px,py,brush*s/2));circle.setColor(0x80664ba0);for(int i=1;i<cells;i++){float d=-brush*s/2+i*step;c.drawLine(px+d,py-brush*s/2,px+d,py+brush*s/2,circle);c.drawLine(px-brush*s/2,py+d,px+brush*s/2,py+d,circle);}c.restoreToCount(clip);}}
        Path circlePath(float x,float y,float r){Path p=new Path();p.addCircle(x,y,r,Path.Direction.CW);return p;}
        public boolean onTouchEvent(MotionEvent e){if(image==null||!isEnabled())return false;int action=e.getActionMasked();if(action==MotionEvent.ACTION_DOWN&&!paintReady())return false;if(!drawing&&!panning&&action!=MotionEvent.ACTION_DOWN){if((action==MotionEvent.ACTION_UP||action==MotionEvent.ACTION_CANCEL)&&getParent()!=null)getParent().requestDisallowInterceptTouchEvent(false);return false;}detector.onTouchEvent(e);getParent().requestDisallowInterceptTouchEvent(true);if(e.getPointerCount()>1){if(action==MotionEvent.ACTION_POINTER_DOWN){drawing=false;panning=true;lastX=(e.getX(0)+e.getX(1))/2;lastY=(e.getY(0)+e.getY(1))/2;}else if(action==MotionEvent.ACTION_MOVE){float x=(e.getX(0)+e.getX(1))/2,y=(e.getY(0)+e.getY(1))/2;panX+=x-lastX;panY+=y-lastY;lastX=x;lastY=y;invalidate();}return true;}if(action==MotionEvent.ACTION_POINTER_UP)return true;if(action==MotionEvent.ACTION_DOWN){panning=false;drawing=true;px=e.getX();py=e.getY();startX=(px-left())/scale();startY=(py-top())/scale();path.reset();path.moveTo(startX,startY);rect.set(startX,startY,startX,startY);if(!rectangle){stroke.setStrokeWidth(brush);maskCanvas.drawPoint(startX,startY,stroke);}invalidate();}else if(action==MotionEvent.ACTION_MOVE&&drawing&&!panning){px=e.getX();py=e.getY();float x=(px-left())/scale(),y=(py-top())/scale();if(rectangle)rect.set(Math.min(startX,x),Math.min(startY,y),Math.max(startX,x),Math.max(startY,y));else{path.lineTo(x,y);stroke.setStrokeWidth(brush);maskCanvas.drawPath(path,stroke);path.reset();path.moveTo(x,y);}invalidate();}else if(action==MotionEvent.ACTION_UP||action==MotionEvent.ACTION_CANCEL){if(drawing&&rectangle&&action==MotionEvent.ACTION_UP){Paint fill=new Paint();fill.setColor(Color.WHITE);maskCanvas.drawRect(rect,fill);}drawing=false;panning=false;getParent().requestDisallowInterceptTouchEvent(false);invalidate();performClick();}return true;}
        public boolean performClick(){super.performClick();return true;}
        Bitmap apply(Files.Job job)throws Exception{if(image==null)return null;Bitmap result=null,effected=null;try{int w=image.getWidth(),h=image.getHeight(),size=Math.max(2,strength);effected=image.copy(Bitmap.Config.ARGB_8888,true);effected.setHasAlpha(true);result=image.copy(Bitmap.Config.ARGB_8888,true);Paint p=new Paint();Canvas c=new Canvas(effected);if(effect==2)c.drawColor(coverColor);else if(effect==1){Bitmap small=Bitmap.createScaledBitmap(image,Math.max(1,w/size),Math.max(1,h/size),true);try{p.setFilterBitmap(true);c.drawBitmap(small,null,new Rect(0,0,w,h),p);}finally{small.recycle();}}else{int[] row=new int[w];for(int y=0;y<h;y+=size){job.check();image.getPixels(row,0,w,0,Math.min(h-1,y+size/2),w,1);for(int x=0;x<w;x+=size){p.setColor(row[Math.min(w-1,x+size/2)]);c.drawRect(x,y,Math.min(w,x+size),Math.min(h,y+size),p);}}}p.reset();p.setXfermode(new PorterDuffXfermode(PorterDuff.Mode.DST_IN));c.drawBitmap(mask,0,0,p);p.reset();new Canvas(result).drawBitmap(effected,0,0,p);job.check();Bitmap ready=result;result=null;return ready;}finally{if(effected!=null)effected.recycle();if(result!=null)result.recycle();}}
        void commitEffect(Bitmap next){if(next==null)return;if(image!=null)image.recycle();image=next;clear();}

    }
}
