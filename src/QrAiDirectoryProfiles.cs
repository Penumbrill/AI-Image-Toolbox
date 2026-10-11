using System;
using System.Collections.Generic;
using System.IO;

namespace QQImageSwitch
{
    public static partial class QrAiService
    {
        // A pipeline name alone cannot distinguish SD 1.x from SD 2.x or an
        // inpainting/refiner architecture. Require the actual UNet contract.
        static string DirectoryFamily(string path,bool control)
        {
            var data=ReadObject(Path.Combine(path,control?"config.json":"model_index.json"));
            if(control)return ControlFamily(data);
            string kind=Value(data,"_class_name"),family;
            if(kind=="StableDiffusionPipeline"||kind=="StableDiffusionImg2ImgPipeline")family="sd15";
            else if(kind=="StableDiffusionXLPipeline"||kind=="StableDiffusionXLImg2ImgPipeline")family="sdxl";
            else return null;
            var unet=ReadObject(Path.Combine(path,"unet","config.json"));
            if(Value(unet,"_class_name")!="UNet2DConditionModel"||!ModelInteger(unet,"in_channels",4)||!ModelInteger(unet,"out_channels",4))return null;
            return ModelInteger(unet,"cross_attention_dim",family=="sdxl"?2048:768)?family:null;
        }
        static bool ModelInteger(Dictionary<string,object> data,string key,int expected)
        {
            object value;if(!data.TryGetValue(key,out value)||value==null)return false;
            // Strings, booleans and lists are not an explicit scalar config.
            if(!(value is int||value is long||value is decimal||value is double||value is float))return false;
            return Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture)==expected;
        }
        static string ControlFamily(Dictionary<string,object> data)
        {
            if(Value(data,"_class_name")!="ControlNetModel"||!ModelInteger(data,"in_channels",4))return null;
            return ModelInteger(data,"cross_attention_dim",2048)?"sdxl":ModelInteger(data,"cross_attention_dim",768)?"sd15":null;
        }
    }
}
