package com.guangyingruoshui.imagetools;

import android.app.AlertDialog;
import android.widget.*;
import java.io.*;
import java.util.*;
import org.json.*;

/** User supplies both files; this never sends a message or assumes QQ compatibility. */
final class QQRoundTrip {
    static JSONObject inspect(File sent,File received,String client,String channel,boolean original,Files.Job job)throws Exception {
        if(sent==null||received==null||!sent.isFile()||!received.isFile())throw new IOException("请选择发送前与收到后的文件 / Select both files");if(client.trim().isEmpty())throw new IOException("请填写 QQ 客户端版本 / Enter the QQ client version");
        String before=Files.hash(sent,job),after=Files.hash(received,job);boolean equal=sent.length()==received.length()&&before.equals(after);
        JSONObject result=new JSONObject().put("schema","qq-roundtrip-v1").put("createdUtc",new java.text.SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'",Locale.ROOT){{setTimeZone(TimeZone.getTimeZone("UTC"));}}.format(new Date())).put("clientVersion",client.trim()).put("channel",channel).put("userConfirmedOriginal",original).put("byteIdentical",equal).put("verifiedOriginalRoundTrip",original&&equal);
        result.put("sent",new JSONObject().put("name",sent.getName()).put("bytes",sent.length()).put("sha256",before));result.put("received",new JSONObject().put("name",received.getName()).put("bytes",received.length()).put("sha256",after));result.put("scope","Only the supplied files and recorded client/channel are tested; no claim about other QQ clients or future transfers.");return result;
    }
    static AlertDialog show(MainActivity a){
        LinearLayout body=a.column();body.setPadding(a.dp(16),a.dp(12),a.dp(16),a.dp(8));body.addView(a.label(a.t("先按原图方式发送，下载收到的文件，再选择两份文件比较。报告只证明此次文件是否被改变，不会自动发送消息。","Send as an original, download the received file, then compare both files. The report checks this transfer only and never sends a message."),13,false));File[] files={null,null};TextView sent=a.label(a.t("未选择发送前文件","No sent file selected"),12,false),received=a.label(a.t("未选择收到后文件","No received file selected"),12,false);body.addView(sent);body.addView(received);
        a.buttons(body,a.button(a.t("选择发送前文件","Choose sent file"),()->a.pick("*/*",false,list->{if(!list.isEmpty()){files[0]=list.get(0);sent.setText(files[0].getName());}})),a.button(a.t("选择收到后文件","Choose received file"),()->a.pick("*/*",false,list->{if(!list.isEmpty()){files[1]=list.get(0);received.setText(files[1].getName());}})));
        EditText version=new EditText(a);version.setTag("qq-client-version");version.setSingleLine(true);version.setHint(a.t("QQ 客户端版本（例如 Android 9.x）","QQ client version (for example Android 9.x)"));body.addView(version);String[] channel={"group"};a.spinner(body,a.t("发送场景","Transfer channel"),new String[]{a.t("群聊","Group"),a.t("私聊","Private chat"),a.t("我的电脑 / 文件传输","File transfer")},0,i->channel[0]=new String[]{"group","private","file-transfer"}[i]);CheckBox original=new CheckBox(a);original.setText(a.t("我已确认发送时选择了原图 / 原文件","I confirmed sending the original image/file"));body.addView(original);TextView result=a.label("",13,true);result.setTextIsSelectable(true);body.addView(result);
        a.buttons(body,a.button(a.t("校验并导出 JSON 报告","Verify and export JSON report"),()->{final File before=files[0],after=files[1];final String client=version.getText().toString(),scene=channel[0];final boolean confirmed=original.isChecked();a.task(job->{JSONObject report=inspect(before,after,client,scene,confirmed,job);File out=Files.unique(a.outputDir,"QQ_往返校验.json");try(OutputStream stream=new FileOutputStream(out)){stream.write(report.toString(2).getBytes(Files.UTF8));}String message=report.getBoolean("byteIdentical")?a.t("两份文件的字节与 SHA-256 一致。","Both files have identical bytes and SHA-256."):a.t("两份文件不同，传输结果未通过原样校验。","Files differ; the transfer failed byte-identity verification.");a.ui.post(()->{if(!a.destroyed)result.setText(message+(confirmed?"":a.t("\n尚未确认原图发送；报告不认定原图往返成功。","\nOriginal sending is not confirmed; the report does not certify an original round trip.")));});return Collections.singletonList(out);},outputs->a.saveOutputs(outputs,MainActivity.FOLDERS[2]));}));ScrollView scroll=new ScrollView(a);scroll.addView(body);return new AlertDialog.Builder(a).setTitle(a.t("QQ 原图往返校验","QQ original round-trip check")).setView(scroll).setPositiveButton(a.t("关闭","Close"),null).show();
    }
}
