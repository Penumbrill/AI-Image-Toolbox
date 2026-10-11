package com.guangyingruoshui.imagetools;

import android.graphics.*;
import android.app.AlertDialog;
import android.content.*;
import android.net.Uri;
import android.view.*;
import android.widget.*;
import java.io.*;
import java.util.*;

final class QrTool {
    final MainActivity a;final MainActivity.State s;Widgets.Preview preview;TextView source,scanResult,artInfo;Button open,useResult;EditText content;AlertDialog selection;String decoded;int ticket;Runnable pending;boolean gone;android.widget.SeekBar horizontal,vertical,qrSizeControl;LinearLayout textPanel,positionPanel,dragPanel,createCard;QrAiPanel aiPanel;String completed;
    QrTool(MainActivity activity,LinearLayout body){
        a=activity;s=a.state[9];
        body.addView(a.label(a.t("填写链接、文本或名片内容，也可从二维码截图导入；图片及提示词全部在本机处理。","Enter a URL, text or vCard, or import a QR screenshot. Images and prompts are processed only on this device."),14,false));
        LinearLayout c=a.card(body,a.t("生成二维码","Create QR code"));createCard=c;
        content=new EditText(a);content.setText(s.qrUrl);content.setSingleLine(false);content.setMinLines(2);content.setMaxLines(4);content.setHorizontallyScrolling(false);content.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_VARIATION_URI|android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE);content.setFilters(new android.text.InputFilter[]{new android.text.InputFilter.LengthFilter(1600)});
        c.addView(a.label(a.t("二维码内容（链接、文本、名片）","QR content (URL, text, vCard)"),14,false));c.addView(content,new LinearLayout.LayoutParams(-1,-2));
        content.addTextChangedListener(new android.text.TextWatcher(){public void beforeTextChanged(CharSequence v,int st,int count,int after){}public void onTextChanged(CharSequence v,int st,int before,int count){s.qrUrl=v.toString();s.qrPreviewCleared=false;schedule();}public void afterTextChanged(android.text.Editable e){}});
        a.buttons(c,a.button(a.t("粘贴内容","Paste content"),()->{try{android.content.ClipboardManager clipboard=(android.content.ClipboardManager)a.getSystemService(Context.CLIPBOARD_SERVICE);ClipData clip=clipboard.getPrimaryClip();String value=clip==null||clip.getItemCount()==0?"":clip.getItemAt(0).coerceToText(a).toString();if(value.isEmpty()){a.toast(a.t("剪贴板中没有文字内容。","The clipboard has no text content."));return;}if(value.length()>1600){a.toast(a.t("剪贴板内容过长，请缩短内容。","Clipboard content is too long. Use shorter content."));return;}content.setText(value);content.setSelection(0);}catch(Exception ex){a.toast(a.t("无法读取剪贴板。","Unable to read the clipboard."));}}));
        a.buttons(c,a.button(a.t("导入截图中的二维码","Import QR from screenshot"),()->importScreenshot(true)),a.button(a.t("清空预览","Clear preview"),this::clearPreview));
        c.addView(a.label(a.t("只提取选中的二维码内容，重新生成后融入底图；不会带入昵称、头像或截图界面。多个二维码会先让你选择。","Only the selected QR content is regenerated onto the base image; screenshot names, avatars and interface elements are excluded. Choose a code when multiple are found."),12,false));
        a.spinner(c,a.t("内容类型","Content type"),new String[]{a.t("网站链接（HTTP(S)）","Website URL (HTTP(S))"),a.t("原始内容（文本、名片）","Exact content (text, vCard)")},s.qrRaw?1:0,i->{s.qrRaw=i==1;s.qrPreviewCleared=false;schedule();},true);
        a.spinner(c,a.t("生成方式","Output mode"),new String[]{a.t("直接生成二维码","QR code only"),a.t("图片附上二维码","QR code on an image"),a.t("像素纹理融合","Pixel texture fusion"),a.t("本地 AI 内嵌二维码","Local AI embedded QR")},s.qrMode,i->{s.qrMode=i;s.qrPreviewCleared=false;updateMode();schedule();},true);
        artInfo=a.label(a.t("内置像素纹理融合，无需联网。保留识别核心，导出自动校验；白边至少 4 格。融合会改变二维码所在区域的颜色。","Built-in pixel fusion works offline. Recognition cores are preserved and export verifies decoding. The border is at least 4 modules. Fusion changes colors in the QR area."),12,false);c.addView(artInfo);
        source=a.label(s.cover==null?a.t("尚未选择图片","No base image selected"):s.cover.getName(),12,false);a.buttons(c,a.button(a.t("选择底图","Choose base image"),this::chooseBase));c.addView(source);
        preview=a.preview(c,a.t("点击选择图片\n或通过“选择底图”导入","Tap to choose an image\nor use Choose base image"));preview.setOnClickListener(v->{if(preview.image==null)chooseBase();});preview.freeText=true;
        preview.move=(x,y)->{if(a.aiController.running)return;if(s.qrDrag==0){s.qrX=Math.round(x);s.qrY=Math.round(y);}else{s.position=0;s.x=x;s.y=y;a.saveText(s);}s.qrPreviewCleared=false;schedule();};
        preview.dragFinished=()->{if(horizontal!=null)horizontal.setProgress(s.qrX);if(vertical!=null)vertical.setProgress(s.qrY);if(s.textUi!=null)s.textUi.run();};preview.reload=this::schedule;
        dragPanel=a.column();c.addView(dragPanel);a.spinner(dragPanel,a.t("拖动对象","Drag target"),new String[]{a.t("二维码","QR code"),a.t("当前文字","Selected text")},s.qrDrag,i->s.qrDrag=i,true);
        positionPanel=a.column();c.addView(positionPanel);qrSizeControl=a.slider(positionPanel,a.t("二维码大小（短边 %）","QR size (% of short edge)"),10,90,s.qrSize,v->{s.qrSize=v;s.qrPreviewCleared=false;schedule();});
        a.slider(positionPanel,a.t("二维码白边（格）","QR white border (modules)"),0,12,s.qrBorder,v->{s.qrBorder=v;s.qrPreviewCleared=false;schedule();});positionPanel.addView(a.label(a.t("建议至少 4 格；更小的白边可能影响手机扫描。","At least 4 modules is recommended. Smaller borders may prevent scanning."),12,false));
        horizontal=a.slider(positionPanel,a.t("二维码横向 %","QR horizontal %"),0,100,s.qrX,v->{s.qrX=v;s.qrPreviewCleared=false;schedule();});vertical=a.slider(positionPanel,a.t("二维码纵向 %","QR vertical %"),0,100,s.qrY,v->{s.qrY=v;s.qrPreviewCleared=false;schedule();});
        aiPanel=new QrAiPanel(this,c);
        textPanel=a.column();c.addView(textPanel);a.textControls(textPanel,s,this::schedule);
        c.addView(a.label(a.t("长按预览拖动定位。背景显图可选择二维码优化；传输平台压缩仍可能影响扫码。","Hold the preview to position the QR or text. Background reveal offers QR optimization; platform compression may still affect scanning."),12,false));
        c.addView(a.label(a.t("二维码结果显示在本页“二维码预览”框，导出前请先查看。","Results appear in the QR preview on this page. Inspect them before export."),12,false));
        a.buttons(c,a.button(a.t("导出二维码 PNG","Export QR PNG"),this::export));
        c.addView(a.label(a.t("图片生成完毕后可添加文字。","You can add text after image generation."),12,false));
        LinearLayout scan=a.card(body,a.t("扫描二维码","Scan QR code"));scan.addView(a.label(a.t("导入透明 PNG 后自动尝试白底与黑底识别，无需手动转换黑底图片。","Imported transparent PNGs are decoded on both white and black automatically."),12,false));
        scanResult=a.label(a.t("尚未扫描","Nothing scanned"),14,false);scanResult.setTextIsSelectable(true);
        open=a.button(a.t("打开扫描的网站","Open scanned website"),()->{try{a.startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse(QrService.website(decoded))).addCategory(Intent.CATEGORY_BROWSABLE));}catch(Exception e){a.toast(a.t("无法打开网站，请复制网址。","Unable to open the website. Copy the URL instead."));}});open.setEnabled(false);
        a.buttons(scan,a.button(a.t("相机扫描","Camera scan"),a::startQrCamera),a.button(a.t("导入图片扫描","Scan imported image"),()->importScreenshot(false)));scan.addView(scanResult);
        useResult=a.button(a.t("将扫描内容用于生成","Use scanned content to generate"),()->applyContent(decoded,false));useResult.setEnabled(false);
        a.buttons(scan,open,useResult,a.button(a.t("复制扫描结果","Copy scan result"),()->{if(decoded!=null)try{((android.content.ClipboardManager)a.getSystemService(Context.CLIPBOARD_SERVICE)).setPrimaryClip(ClipData.newPlainText("QR",decoded));}catch(Exception ex){a.toast(a.t("复制失败，请手动选择复制。","Copy failed; select and copy the text manually."));}}));updateMode();schedule();
    }
    void chooseBase(){if(a.busy||a.aiController.running)return;a.pick("image/*",false,files->{if(files.isEmpty()||gone)return;s.cover=files.get(0);s.ai.clearResult();s.qrPreviewCleared=false;source.setText(s.cover.getName());if(s.qrMode==0)s.qrMode=1;a.rebuild();});}
    void clearPreview(){if(a.busy||a.aiController.running)return;stop();completed=null;s.qrPreviewCleared=true;s.cover=null;s.ai.clearResult();preview.set(null);source.setText(a.t("尚未选择图片","No base image selected"));a.message(a.t("预览已清空，二维码内容、模型和文字设置已保留。","Preview cleared; QR content, models and text settings are retained."));}

    void importScreenshot(boolean forGeneration){
        a.pick("image/*",false,files->{
            if(files.isEmpty()||gone||a.destroyed||a.page!=9)return;
            final ArrayList<QrService.Match> found=new ArrayList<>();
            a.task(j->{Bitmap image=Images.load(files.get(0),3200,false);try{found.addAll(QrService.scanImported(image,j));}finally{image.recycle();}return Collections.emptyList();},outputs->{
                if(gone||a.destroyed||a.page!=9)return;
                if(found.isEmpty()){setResult(null);return;}
                if(found.size()==1){accept(found.get(0),forGeneration);return;}
                String[] labels=new String[found.size()];
                for(int i=0;i<labels.length;i++){QrService.Match item=found.get(i);String summary=item.payload.replace('\r',' ').replace('\n',' ').replace('\t',' ');if(summary.length()>70)summary=summary.substring(0,70)+"…";labels[i]=(i+1)+" · "+summary+"\n"+a.t("位置 ","Position ")+item.left+","+item.top;}
                if(selection!=null)selection.dismiss();
                selection=new AlertDialog.Builder(a).setTitle(a.t("发现多个二维码，请选择","Multiple QR codes found; choose one")).setItems(labels,(dialog,index)->{if(!gone&&!a.destroyed&&a.page==9)accept(found.get(index),forGeneration);}).setNegativeButton(a.t("取消","Cancel"),null).create();
                selection.setOnDismissListener(dialog->selection=null);selection.show();
            });
        });
    }
    void accept(QrService.Match match,boolean forGeneration){setResult(match.payload);if(forGeneration)applyContent(match.payload,true);}
    void applyContent(String value,boolean fuse){
        try{String exact=QrService.payload(value);s.qrRaw=true;content.setText(exact);content.setSelection(0);if(fuse&&s.qrMode!=3)s.qrMode=2;a.rebuild();if(a.qrTool!=null)a.qrTool.setResult(exact);a.toast(s.cover==null?a.t("已导入二维码内容；选择底图后即可融合。","QR content imported. Choose a base image to fuse it."):a.t("已导入二维码内容，保留当前底图。","QR content imported; the current base image is retained."));}
        catch(Exception error){a.toast(a.error(error));}
    }
    void updateMode(){boolean overlay=s.qrMode!=0,ai=s.qrMode==3,automatic=ai&&s.ai.autoPlace;
        if(artInfo!=null)artInfo.setVisibility(s.qrMode==2?View.VISIBLE:View.GONE);if(textPanel!=null)textPanel.setVisibility(overlay?View.VISIBLE:View.GONE);
        if(aiPanel!=null)aiPanel.body.setVisibility(ai?View.VISIBLE:View.GONE);if(positionPanel!=null)positionPanel.setVisibility(automatic?View.GONE:View.VISIBLE);if(dragPanel!=null)dragPanel.setVisibility(automatic?View.GONE:View.VISIBLE);
        boolean enabled=overlay&&!a.aiController.running;if(qrSizeControl!=null)qrSizeControl.setEnabled(enabled);if(horizontal!=null)horizontal.setEnabled(enabled);if(vertical!=null)vertical.setEnabled(enabled);if(preview!=null)preview.freeText=overlay&&!automatic;
    }
    Bitmap generate(MainActivity.State options,boolean full,Files.Job j)throws Exception{
        if(options.qrMode==3&&options.ai.result!=null&&options.ai.result.isFile()&&AndroidAiController.key(options,options.ai).equals(options.ai.resultKey)){
            Bitmap b=Images.load(options.ai.result,full?0:1200,full);try{a.drawTexts(b,options,1);return b;}catch(Exception e){b.recycle();throw e;}
        }
        if(options.qrMode==3&&full)throw new IOException(a.t("请先点击一键 AI 生成，位置或设置改变后需重新生成。","Generate the AI image first. Regenerate after changing position or settings."));
        if(options.qrMode==0){int modules=QrService.matrix(options.qrUrl,1,options.qrBorder,options.qrRaw).getWidth();return QrService.code(options.qrUrl,Math.max(4,1024/modules)*modules,options.qrBorder,options.qrRaw);}
        if(options.cover==null)throw new IOException(a.t("请先选择底图。","Choose a base image first."));Bitmap original=Images.load(options.cover,full?0:1200,full),b;
        try{b=Images.fit(original,original.getWidth(),original.getHeight(),Color.WHITE);}finally{original.recycle();}
        try{a.drawTexts(b,options,1);if(options.qrMode==3&&options.ai.autoPlace)return b;
            if(options.qrMode==2)QrArt.compose(b,options.qrUrl,options.qrSize,options.qrX,options.qrY,options.qrBorder,j,options.qrRaw);
            else QrService.compose(b,options.qrUrl,options.qrSize,options.qrX,options.qrY,false,options.qrBorder,options.qrRaw);return b;
        }catch(Exception e){b.recycle();throw e;}
    }
    String key(){a.saveText(s);StringBuilder key=new StringBuilder(AndroidAiController.key(s,s.ai)).append(s.ai.result==null?"":s.ai.result.getPath()).append(s.qrUrl).append(s.qrRaw).append(s.qrMode).append(s.qrBorder).append(s.qrSize).append(s.qrX).append(s.qrY).append(s.numbered).append(s.cover==null?"":s.cover.getPath()+s.cover.lastModified());for(MainActivity.TextItem i:s.texts)key.append(i.text).append(i.position).append(i.size).append(i.x).append(i.y).append(i.color);return key.toString();}
    void schedule(){if(gone||preview==null||s.qrPreviewCleared)return;if(s.qrMode!=0&&s.cover==null){stop();preview.set(null);completed=null;return;}String requested=key();if(preview.image!=null&&requested.equals(completed))return;int version=++ticket;if(pending!=null)a.ui.removeCallbacks(pending);pending=()->{MainActivity.State options=a.copyOptions(s);a.worker.execute(()->{if(gone||a.destroyed||ticket!=version)return;Bitmap b=null;String error=null;try{b=generate(options,false,new Files.Job());}catch(Exception e){error=a.error(e);}final Bitmap image=b;final String message=error;a.ui.post(()->{if(gone||a.destroyed||a.page!=9||ticket!=version){if(image!=null)image.recycle();return;}preview.set(image);if(image!=null)completed=requested;updateMode();if(message!=null)a.message(message);});});};a.ui.postDelayed(pending,preview.dragging?20:250);}
    void export(){MainActivity.State options=a.copyOptions(s);a.lastFeature=MainActivity.FOLDERS[9];a.task(j->{Bitmap b=generate(options,true,j);File tmp=Files.temp(a.outputDir),out=Files.unique(a.outputDir,"qr.png");try{String url=QrService.content(options.qrUrl,options.qrRaw);if(!QrService.matches(b,url,j))throw new IOException(a.t("二维码自检失败，请增加大小。","QR self-check failed. Increase its size."));QrService.Region region;if(options.qrMode==3){region=options.ai.region;if(region==null)throw new IOException("Missing AI placement metadata");}else if(options.qrMode==0){region=new QrService.Region();region.url=url;region.raw=options.qrRaw;region.width=b.getWidth();region.height=b.getHeight();region.size=b.getWidth();region.margin=options.qrBorder;}else region=QrService.position(b,url,options.qrSize,options.qrX,options.qrY,options.qrMode==2?Math.max(4,options.qrBorder):options.qrBorder,options.qrRaw);Images.encode(b,tmp,0,j);QrService.tag(tmp,region,j);Files.commit(tmp,out,j);return Collections.singletonList(out);}finally{b.recycle();tmp.delete();}},files->a.saveOutputs(files,MainActivity.FOLDERS[9]));}
    void setResult(String value){decoded=value;useResult.setEnabled(value!=null&&!value.isEmpty());scanResult.setText(value==null?a.t("未识别可解码的标准二维码。请使用清晰原图；微信小程序码等专有图码不支持。","No decodable standard QR found. Use a clear original image; proprietary codes such as WeChat Mini Program codes are unsupported."):value);open.setEnabled(false);try{String u=QrService.website(value);scanResult.setText(new java.net.URI(u).getHost()+"\n"+value);open.setEnabled(true);}catch(Exception ignored){} }
    void stop(){ticket++;if(pending!=null)a.ui.removeCallbacks(pending);if(selection!=null){selection.dismiss();selection=null;}}
    void release(){gone=true;stop();if(aiPanel!=null)aiPanel.detach();}
}
