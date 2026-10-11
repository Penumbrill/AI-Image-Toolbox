package com.guangyingruoshui.imagetools;

import android.graphics.Bitmap;
import ai.onnxruntime.*;
import java.io.*;
import java.nio.*;
import java.util.*;
import java.util.regex.Pattern;

/** Local ONNX recognition. No image, prompt or tensor is sent to a server.
 * BLIP tensor names follow ningpp/flux's exported BLIP encoder/decoder.
 * SegFormer preprocessing and labels follow Xenova/segformer_b2_clothes.
 * Face letterboxing and NMS follow the desktop worker's anime-face guard.
 */
final class AiVision {
    private static final class RuntimeState { static final OrtEnvironment ENV=OrtEnvironment.getEnvironment(); }
    private static OrtEnvironment env(){return RuntimeState.ENV;}
    private static final float[] BLIP_MEAN={.48145466f,.4578275f,.40821073f},BLIP_STD={.26862954f,.26130258f,.27577711f};
    private static final float[] SEG_MEAN={.485f,.456f,.406f},SEG_STD={.229f,.224f,.225f};
    static final String NEGATIVE="low quality, blurry, watermark, text, logo, deformed face, distorted anatomy, horror, grotesque, organic holes, tentacles, spirals, chaotic repetitive pattern";

    static final class MaterialMap {
        final int width,height;
        final byte[] labels,protectedMask,identityMask,skinMask;
        MaterialMap(int w,int h,byte[] values){
            width=w;height=h;labels=values;byte[] identity=new byte[values.length],all=new byte[values.length],skin=new byte[values.length];
            for(int i=0;i<values.length;i++){int label=values[i]&255;if(identityLabel(label))identity[i]=(byte)255;if(protectedLabel(label))all[i]=(byte)255;if(skinLabel(label))skin[i]=(byte)255;}
            identityMask=dilate(identity,w,h,5);protectedMask=dilate(all,w,h,5);skinMask=erode(skin,w,h,3);
            for(int i=0;i<skinMask.length;i++)if(identityMask[i]!=0)skinMask[i]=0;
        }
        boolean isFaceOrHair(int x,int y){return !inside(x,y)||identityMask[y*width+x]!=0;}
        boolean isSkin(int x,int y){return inside(x,y)&&skinLabel(labels[y*width+x]&255);}
        boolean isProtected(int x,int y){return !inside(x,y)||protectedMask[y*width+x]!=0;}
        boolean inside(int x,int y){return x>=0&&y>=0&&x<width&&y<height;}
        byte[] editableMask(boolean manualSkin,int left,int top,int side){
            byte[] mask=new byte[labels.length];for(int y=0;y<height;y++)for(int x=0;x<width;x++){int p=y*width+x;boolean requested=x>=left&&y>=top&&x<left+side&&y<top+side;mask[p]=(byte)((protectedMask[p]==0||(manualSkin&&requested&&skinMask[p]!=0))?255:0);}return mask;
        }
    }
    static boolean identityLabel(int label){return label==2||label==3||label==11;}
    static boolean skinLabel(int label){return label>=12&&label<=15;}
    static boolean protectedLabel(int label){return identityLabel(label)||skinLabel(label);}

    static String caption(Bitmap image,File root,Files.Job job)throws Exception {
        File folder=new File(root,"caption/blip-base");return caption(image,model(folder,"blip_vision_encoder.onnx"),model(folder,"blip_text_decoder.onnx"),model(folder,"vocab.txt"),job);
    }
    static String caption(Bitmap image,File visionFile,File decoderFile,File vocabularyFile,Files.Job job)throws Exception {
        job.check();float[] embeddings;long[] embeddingShape;
        try(OrtSession.SessionOptions options=options();OrtSession vision=env().createSession(model(visionFile).getAbsolutePath(),options);
            OnnxTensor pixels=OnnxTensor.createTensor(env(),FloatBuffer.wrap(preprocess(image,384,384,BLIP_MEAN,BLIP_STD,true)),new long[]{1,3,384,384});
            OrtSession.Result result=vision.run(Collections.singletonMap("pixel_values",pixels))){
            OnnxTensor output=tensor(result.get(0));embeddingShape=output.getInfo().getShape();if(embeddingShape.length!=3||embeddingShape[0]!=1||embeddingShape[2]!=768)throw new IOException("BLIP 图像编码模型不匹配 / Incompatible BLIP vision encoder");embeddings=floats(output);job.check();
        }
        List<String> vocab=lines(model(vocabularyFile));if(vocab.size()==30522){vocab.add("[DEC]");vocab.add("[ENC]");}if(vocab.size()!=30524)throw new IOException("BLIP 词表不完整 / Incomplete BLIP vocabulary");
        long[] sequence=new long[51];sequence[0]=30522;int length=1;
        try(OrtSession.SessionOptions options=options();OrtSession decoder=env().createSession(model(decoderFile).getAbsolutePath(),options);
            OnnxTensor encoded=OnnxTensor.createTensor(env(),FloatBuffer.wrap(embeddings),embeddingShape)){
            for(int step=0;step<50;step++){
                job.check();Map<String,OnnxTensor> inputs=new HashMap<>();
                try(OnnxTensor ids=OnnxTensor.createTensor(env(),LongBuffer.wrap(Arrays.copyOf(sequence,length)),new long[]{1,length})){
                    inputs.put("input_ids",ids);inputs.put("encoder_hidden_states",encoded);
                    try(OrtSession.Result result=decoder.run(inputs)){
                        OnnxTensor output=tensor(result.get(0));long[] shape=output.getInfo().getShape();if(shape.length!=3||shape[0]!=1||shape[1]!=length||shape[2]!=vocab.size())throw new IOException("BLIP 文字模型不匹配 / Incompatible BLIP text decoder");
                        FloatBuffer logits=output.getFloatBuffer();int size=(int)shape[2],offset=(length-1)*size,best=0;float maximum=-Float.MAX_VALUE;
                        for(int i=0;i<size;i++){float value=logits.get(offset+i);if(value>maximum){maximum=value;best=i;}}if(best==102)break;sequence[length++]=best;
                    }
                }
            }
        }
        job.check();String text=decode(sequence,length,vocab);if(text.trim().isEmpty())throw new IOException("本地识图未生成描述 / Local captioning produced no description");return text;
    }

    static MaterialMap materials(Bitmap image,File root,Files.Job job)throws Exception {
        return materials(image,model(new File(root,"segmentation/clothes"),"model.onnx"),model(new File(root,"protection/anime-face"),"model.onnx"),job);
    }
    static MaterialMap materials(Bitmap image,File segmentationFile,File faceFile,Files.Job job)throws Exception {
        job.check();int width=image.getWidth(),height=image.getHeight();if((long)width*height>24_000_000)throw new IOException("图片过大，请降低尺寸 / Image too large; reduce its resolution");byte[] labels;
        try(OrtSession.SessionOptions options=options();OrtSession session=env().createSession(model(segmentationFile).getAbsolutePath(),options);
            OnnxTensor input=OnnxTensor.createTensor(env(),FloatBuffer.wrap(preprocess(image,512,512,SEG_MEAN,SEG_STD,false)),new long[]{1,3,512,512});
            OrtSession.Result result=session.run(Collections.singletonMap("pixel_values",input))){
            OnnxTensor output=tensor(result.get(0));long[] shape=output.getInfo().getShape();if(shape.length!=4||shape[0]!=1||shape[1]!=18||shape[2]<1||shape[3]<1)throw new IOException("人体分区模型不匹配 / Incompatible human parser");labels=resizeArgmax(floats(output),18,(int)shape[3],(int)shape[2],width,height,job);
        }
        // Full-image segmentation can miss small illustrated faces. Keep the same
        // independent, expanded ONNX face boxes used by the Windows implementation.
        faceGuards(image,faceFile,labels,job);job.check();return new MaterialMap(width,height,labels);
    }
    static Bitmap materialMask(Bitmap image,File root,Files.Job job)throws Exception {
        MaterialMap map=materials(image,root,job);int[] colors=new int[map.labels.length];for(int i=0;i<colors.length;i++)colors[i]=map.protectedMask[i]==0?0xffffffff:0xff000000;return Bitmap.createBitmap(colors,map.width,map.height,Bitmap.Config.ARGB_8888);
    }

    private static void faceGuards(Bitmap image,File faceFile,byte[] labels,Files.Job job)throws Exception {
        int width=image.getWidth(),height=image.getHeight();double fit=Math.min(1,Math.min(640.0/width,640.0/height));int rw=Math.max(1,(int)Math.round(width*fit)),rh=Math.max(1,(int)Math.round(height*fit)),ox=(640-rw)/2,oy=(640-rh)/2;
        float[] resized=preprocess(image,rw,rh,new float[]{0,0,0},new float[]{1,1,1},true),input=new float[3*640*640];Arrays.fill(input,114f/255f);
        for(int c=0;c<3;c++)for(int y=0;y<rh;y++)System.arraycopy(resized,c*rw*rh+y*rw,input,c*640*640+(oy+y)*640+ox,rw);
        List<float[]> candidates=new ArrayList<>();
        try(OrtSession.SessionOptions options=options();OrtSession session=env().createSession(model(faceFile).getAbsolutePath(),options);
            OnnxTensor pixels=OnnxTensor.createTensor(env(),FloatBuffer.wrap(input),new long[]{1,3,640,640});
            OrtSession.Result result=session.run(Collections.singletonMap(session.getInputNames().iterator().next(),pixels))){
            OnnxTensor output=tensor(result.get(0));long[] shape=output.getInfo().getShape();if(shape.length!=3||shape[0]!=1||shape[1]!=5)throw new IOException("人脸保护模型不匹配 / Incompatible face-protection model");float[] values=floats(output);int count=(int)shape[2];
            for(int i=0;i<count;i++){float confidence=values[4*count+i];if(confidence<=.3f)continue;float cx=values[i],cy=values[count+i],bw=values[2*count+i],bh=values[3*count+i];if(bw>0&&bh>0)candidates.add(new float[]{cx-bw/2,cy-bh/2,cx+bw/2,cy+bh/2,confidence});}
        }
        candidates.sort((a,b)->Float.compare(b[4],a[4]));List<float[]> kept=new ArrayList<>();
        for(float[] box:candidates){job.check();boolean duplicate=false;for(float[] previous:kept)if(iou(box,previous)>.45f){duplicate=true;break;}if(!duplicate)kept.add(box);}
        for(float[] box:kept){float bw=box[2]-box[0],bh=box[3]-box[1];int left=Math.max(0,Math.min(width,(int)Math.floor((box[0]-ox-bw*.2)*width/rw))),top=Math.max(0,Math.min(height,(int)Math.floor((box[1]-oy-bh*.45)*height/rh))),right=Math.max(0,Math.min(width,(int)Math.ceil((box[2]-ox+bw*.2)*width/rw))),bottom=Math.max(0,Math.min(height,(int)Math.ceil((box[3]-oy+bh*.2)*height/rh)));if(right<=left||bottom<=top)continue;for(int y=top;y<bottom;y++)Arrays.fill(labels,y*width+left,y*width+right,(byte)3);}
    }

    private static OrtSession.SessionOptions options()throws OrtException {
        OrtSession.SessionOptions options=new OrtSession.SessionOptions();options.setIntraOpNumThreads(Math.max(1,Math.min(4,Runtime.getRuntime().availableProcessors())));options.setInterOpNumThreads(1);options.setMemoryPatternOptimization(false);options.setOptimizationLevel(OrtSession.SessionOptions.OptLevel.BASIC_OPT);return options;
    }
    private static File model(File folder,String name)throws IOException {return model(new File(folder,name));}
    private static File model(File file)throws IOException {if(file==null||!file.isFile()||file.length()<1024)throw new IOException("识图模型缺失，请检查并修复模型 / Recognition model missing; check and repair models: "+(file==null?"":file.getAbsolutePath()));return file;}
    private static OnnxTensor tensor(OnnxValue value)throws IOException {if(!(value instanceof OnnxTensor))throw new IOException("ONNX 输出不是张量 / ONNX output is not a tensor");return (OnnxTensor)value;}
    private static float[] floats(OnnxTensor tensor)throws OrtException {FloatBuffer buffer=tensor.getFloatBuffer();float[] values=new float[buffer.remaining()];buffer.get(values);return values;}
    private static List<String> lines(File file)throws IOException {List<String> words=new ArrayList<>();try(BufferedReader reader=new BufferedReader(new InputStreamReader(new FileInputStream(file),"UTF-8"))){String line;while((line=reader.readLine())!=null)words.add(line);}return words;}
    static String decode(long[] tokens,int length,List<String> vocab){StringBuilder text=new StringBuilder();for(int i=0;i<length;i++){int id=(int)tokens[i];if(id<0||id>=vocab.size())continue;String word=vocab.get(id);if(word.startsWith("[")&&word.endsWith("]"))continue;if(word.startsWith("##"))text.append(word.substring(2));else{if(text.length()>0)text.append(' ');text.append(word);}}return text.toString().replace(" .",".").replace(" ,",",").replace(" !","!").replace(" ?","?").replace(" ' ","'");}

    static float[] preprocess(Bitmap image,int width,int height,float[] mean,float[] std,boolean cubic){
        int sw=image.getWidth(),sh=image.getHeight();int[] pixels=new int[sw*sh];image.getPixels(pixels,0,sw,0,0,sw,sh);float[] values=new float[3*width*height];int area=width*height;
        for(int y=0;y<height;y++){double sy=(y+.5)*sh/height-.5;int iy=(int)Math.floor(sy);for(int x=0;x<width;x++){double sx=(x+.5)*sw/width-.5;int ix=(int)Math.floor(sx);double[] rgb=new double[3];double total=0;int from=cubic?-1:0,to=cubic?2:1;
            for(int dy=from;dy<=to;dy++)for(int dx=from;dx<=to;dx++){double weight=(cubic?cubic(sx-ix-dx):Math.max(0,1-Math.abs(sx-ix-dx)))*(cubic?cubic(sy-iy-dy):Math.max(0,1-Math.abs(sy-iy-dy)));int color=pixels[Math.max(0,Math.min(sh-1,iy+dy))*sw+Math.max(0,Math.min(sw-1,ix+dx))],alpha=color>>>24;total+=weight;for(int c=0;c<3;c++){int channel=(color>>>(16-8*c))&255;rgb[c]+=weight*(channel*alpha/255.0+255-alpha);}}
            int p=y*width+x;for(int c=0;c<3;c++)values[c*area+p]=(float)((Math.max(0,Math.min(255,rgb[c]/total))/255-mean[c])/std[c]);
        }}return values;
    }
    private static double cubic(double value){double a=Math.abs(value);return a<=1?1.5*a*a*a-2.5*a*a+1:a<2?-.5*a*a*a+2.5*a*a-4*a+2:0;}
    static byte[] resizeArgmax(float[] logits,int classes,int sw,int sh,int width,int height,Files.Job job)throws IOException {
        byte[] labels=new byte[width*height];int area=sw*sh;for(int y=0;y<height;y++){if((y&15)==0)job.check();double fy=(y+.5)*sh/height-.5;int iy=(int)Math.floor(fy),y0=Math.max(0,Math.min(sh-1,iy)),y1=Math.max(0,Math.min(sh-1,iy+1));float wy=(float)(fy-iy);for(int x=0;x<width;x++){double fx=(x+.5)*sw/width-.5;int ix=(int)Math.floor(fx),x0=Math.max(0,Math.min(sw-1,ix)),x1=Math.max(0,Math.min(sw-1,ix+1)),best=0;float wx=(float)(fx-ix),maximum=-Float.MAX_VALUE;
            for(int c=0;c<classes;c++){int start=c*area;float top=logits[start+y0*sw+x0]*(1-wx)+logits[start+y0*sw+x1]*wx,bottom=logits[start+y1*sw+x0]*(1-wx)+logits[start+y1*sw+x1]*wx,value=top*(1-wy)+bottom*wy;if(value>maximum){maximum=value;best=c;}}labels[y*width+x]=(byte)best;
        }}return labels;
    }
    private static float iou(float[] a,float[] b){float w=Math.max(0,Math.min(a[2],b[2])-Math.max(a[0],b[0])),h=Math.max(0,Math.min(a[3],b[3])-Math.max(a[1],b[1])),intersection=w*h;return intersection/Math.max(1,(a[2]-a[0])*(a[3]-a[1])+(b[2]-b[0])*(b[3]-b[1])-intersection);}
    private static byte[] dilate(byte[] values,int w,int h,int radius){byte[] row=new byte[values.length],result=new byte[values.length];for(int y=0;y<h;y++){int count=0;for(int x=0;x<=Math.min(w-1,radius);x++)if(values[y*w+x]!=0)count++;for(int x=0;x<w;x++){row[y*w+x]=(byte)(count>0?255:0);if(x-radius>=0&&values[y*w+x-radius]!=0)count--;if(x+radius+1<w&&values[y*w+x+radius+1]!=0)count++;}}for(int x=0;x<w;x++){int count=0;for(int y=0;y<=Math.min(h-1,radius);y++)if(row[y*w+x]!=0)count++;for(int y=0;y<h;y++){result[y*w+x]=(byte)(count>0?255:0);if(y-radius>=0&&row[(y-radius)*w+x]!=0)count--;if(y+radius+1<h&&row[(y+radius+1)*w+x]!=0)count++;}}return result;}
    private static byte[] erode(byte[] values,int w,int h,int radius){byte[] inverted=new byte[values.length];for(int i=0;i<values.length;i++)inverted[i]=(byte)(values[i]==0?255:0);byte[] result=dilate(inverted,w,h,radius);for(int i=0;i<result.length;i++)result[i]=(byte)(result[i]==0?255:0);return result;}

    static boolean captionHas(String description,String... terms){String text=description.toLowerCase(Locale.ROOT);for(String term:terms)if(Pattern.compile("(?<!\\w)"+Pattern.quote(term)+"(?:s|es)?(?!\\w)").matcher(text).find())return true;return false;}
    static String visualStyle(String description){String text=description.toLowerCase(Locale.ROOT);for(String word:new String[]{"anime","cartoon","drawing","illustration"})if(text.contains(word))return "illustration, clean coherent linework, matching original shading";for(String word:new String[]{"photo","photograph","photography"})if(text.contains(word))return "photograph, realistic surface detail, natural lighting";return "matching the original visual style, consistent colors and lighting";}
    static String makePrompt(String description,String localDescription){
        String content=localDescription+" "+description;boolean garment=captionHas(content,"sweater","vest","dress","shirt","fabric","coat","clothing","uniform","jacket","garment");String material;
        if(garment){material="original clothing design and colors, existing fabric weave, natural garment folds";if(captionHas(content,"knit","knitted","sweater","wool"))material+=", existing knitted wool yarn";material+=", original fabric palette";for(String pattern:new String[]{"leopard","animal print","floral","striped","plaid","polka dot"})if(captionHas(content,pattern))material+=", existing "+pattern+" fabric pattern";}else material="coherent surface detail, layered natural light and shadow";
        if(captionHas(content,"city","cities","building","street","house","architecture"))material+=", existing architecture, varied window frames, reflections, coherent building details";if(captionHas(content,"tree","forest","garden","plant","foliage","leaf","leaves","blossom")||(!garment&&captionHas(content,"flower")))material+=", existing foliage, overlapping leaves and branches, natural foliage detail";
        return "preserve the original scene and subject, "+material+", "+visualStyle(description+" "+localDescription);
    }
    static String makePrompt(String description,String localDescription,String family){String prompt=makePrompt(description,localDescription);return "sdxl".equals(family)?prompt+". Preserve the source subject, face, pose, clothing design, colors and composition. Blend the encoded light and shade into the existing surface texture.":prompt;}
    static String materialPrompt(String description,String local,String family,boolean skin){
        String style=visualStyle(description+" "+local),detail;
        if(skin)detail="original skin surface and flesh tone, existing fine skin texture, coherent soft illumination, subtle natural highlights and shadows, unchanged anatomy and outline, original pose and subject identity";
        else if(captionHas(local,"sweater","vest","dress","shirt","fabric","coat","clothing","uniform","jacket","garment","knit","knitted","wool")){
            boolean knitted=captionHas(local,"knit","knitted","sweater","wool");
            detail=(knitted?"existing ribbed wool yarn":"original garment material and surface detail")+", original fabric colors, ";
            String pattern="original fabric pattern";for(String value:new String[]{"leopard","animal print","floral","striped","plaid","polka dot"})if(captionHas(local,value)){pattern="existing "+value+" fabric pattern";break;}
            detail+=pattern+", "+(knitted?"continuous original fibers, subtle woven highlights and shadows":"continuous original material, original highlights and shadows")+", unchanged garment design and silhouette, original natural folds";
        }else{
            List<String> surfaces=new ArrayList<>();
            if(captionHas(local,"city","cities","building","street","architecture","balcony","balconies","window frame"))surfaces.add("original building facades, varied window frames, coherent architectural reflections");
            if(captionHas(local,"tree","leaf","leaves","foliage","blossom","flower","garden","plant"))surfaces.add("original branches, overlapping leaves and blossom petals, natural foliage shadows");
            if(captionHas(local,"room","indoor","wood","wooden","furniture"))surfaces.add("original furniture and wood grain, consistent room perspective");
            if(captionHas(local,"sea","ocean","water","beach"))surfaces.add("original ripples and rocks, soft reflected daylight");
            if(captionHas(local,"mountain","landscape","hill"))surfaces.add("original textured rocks and layered landscape contours");
            detail=surfaces.isEmpty()?"existing scene surfaces and textures":android.text.TextUtils.join(", ",surfaces);
            detail+=", original colors, unchanged scene layout and edges, subtle natural light and shadow";
        }
        String prompt=style+", "+detail;
        return "sdxl".equals(family)?prompt+". Preserve the original composition and surface. Encode light and shade through existing material detail.":prompt;
    }
    static String materialNegative(String existing){return existing+", sticker, label, pasted patch, checkerboard, geometric tiles, rectangular panel, plain white backing, recolored leaves, changed architecture";}
    private AiVision(){}
}
