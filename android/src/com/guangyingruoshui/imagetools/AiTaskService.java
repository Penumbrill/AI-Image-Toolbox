package com.guangyingruoshui.imagetools;

import android.app.*;
import android.content.*;
import android.os.*;
import java.lang.ref.WeakReference;

/** Keeps a user-started download/generation task scheduled while the screen is off. */
public final class AiTaskService extends Service {
    static WeakReference<AndroidAiController> active=new WeakReference<>(null);
    static final String CHANNEL="ai-tasks";static final int ID=31;
    PowerManager.WakeLock wake;
    @Override public void onCreate(){super.onCreate();NotificationManager manager=(NotificationManager)getSystemService(NOTIFICATION_SERVICE);if(Build.VERSION.SDK_INT>=26)manager.createNotificationChannel(new NotificationChannel(CHANNEL,"Local AI tasks",NotificationManager.IMPORTANCE_LOW));startForeground(ID,notification("Local AI task is running on this device"));wake=((PowerManager)getSystemService(POWER_SERVICE)).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK,getPackageName()+":ai");wake.acquire(6L*60*60*1000);}
    Notification notification(String message){Notification.Builder builder=Build.VERSION.SDK_INT>=26?new Notification.Builder(this,CHANNEL):new Notification.Builder(this);return builder.setSmallIcon(android.R.drawable.stat_sys_download).setContentTitle("AI Image Editing Tools").setContentText(message).setOngoing(true).build();}
    @Override public int onStartCommand(Intent intent,int flags,int id){return START_NOT_STICKY;}
    @Override public IBinder onBind(Intent intent){return null;}
    @Override public void onTimeout(int startId,int type){AndroidAiController controller=active.get();if(controller!=null)controller.cancel();stopSelf();}
    @Override public void onDestroy(){if(wake!=null&&wake.isHeld())wake.release();stopForeground(true);super.onDestroy();}
    static void start(MainActivity activity,AndroidAiController controller){active=new WeakReference<>(controller);Intent intent=new Intent(activity,AiTaskService.class);if(Build.VERSION.SDK_INT>=26)activity.startForegroundService(intent);else activity.startService(intent);}
    static void stop(MainActivity activity){activity.stopService(new Intent(activity,AiTaskService.class));active.clear();}
}
