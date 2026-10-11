using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QQImageSwitch
{
    public sealed class QrAiModel
    {
        public string Path,Name,Family;public bool Control;public string Integrity;
        public override string ToString(){return Name+" · "+(Family=="sdxl"?"SDXL":"SD 1.5")+(string.IsNullOrEmpty(Integrity)?"":" · "+L.T(Integrity));}
    }
    public sealed class QrAiOptions
    {
        public string Root=QrAiStorage.DefaultRoot(),Source="auto",Policy="auto",Base,Control,Prompt="",Negative="",Family="sd15",Environment="cuda";
        public Dictionary<string,QrAiSelection> Selections=new Dictionary<string,QrAiSelection>();
        public Dictionary<string,QrAiGenerationProfile> Profiles=new Dictionary<string,QrAiGenerationProfile>();
        public bool AutoRoot=true;
        public bool AutoPrompt=true,AutoBackground=false,AutoPlace=false;
        public int Edge;
    }
    public sealed class QrAiHardware
    {
        public bool cuda;public double total_gb,free_gb;public string device;public string[] missing;
    }
    public sealed class QrAiScanException : InvalidOperationException
    {
        public QrAiScanException(string message):base(message){}
    }
    public static partial class QrAiService
    {
        const string PythonUrl="https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip",PythonHash="009d6bf7e3b2ddca3d784fa09f90fe54336d5b60f0e0f305c37f400bf83cfd3b";
        const string PythonPaths="python311.zip\n.\nLib/site-packages\nimport site\n";
        public static string RuntimeFolder(string root,string environment="cuda"){if(environment!="cuda"&&environment!="cpu")throw new ArgumentException("请选择支持的生图环境。");return environment=="cpu"?System.IO.Path.Combine(root,"runtime","cpu"):System.IO.Path.Combine(root,"runtime");}
        public static string Python(string root,string environment="cuda"){return System.IO.Path.Combine(RuntimeFolder(root,environment),"python","python.exe");}
        public static string Materialize(string root)
        {
            QrAiStorage.Register(root);
            string folder=System.IO.Path.Combine(root,"engine");Directory.CreateDirectory(folder);
            foreach(var pair in new[]{new[]{"AiQrWorker","local_qr.py"},new[]{"AiQrModels","models.json"}})
            {
                using(var input=typeof(QrAiService).Assembly.GetManifestResourceStream(pair[0]))using(var bytes=new MemoryStream()){input.CopyTo(bytes);Codec.SaveAtomic(System.IO.Path.Combine(folder,pair[1]),bytes.ToArray());}
            }
            return System.IO.Path.Combine(folder,"local_qr.py");
        }
        static string Hash(Stream stream){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        static string Hash(string path){using(var stream=File.OpenRead(path))return Hash(stream);}
        static string RuntimeEntryPath(string destination,string entry)
        {
            string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(destination,entry));
            if(!path.StartsWith(destination+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("运行环境路径无效。");
            return path;
        }
        static bool RuntimeEntryMatches(ZipArchiveEntry entry,string path)
        {
            if(!File.Exists(path)||new FileInfo(path).Length!=entry.Length)return false;
            using(var input=entry.Open())return Hash(input)==Hash(path);
        }
        static bool PythonRuntimeComplete(string root,string environment="cuda")
        {
            string destination=System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(Python(root,environment))),zip=System.IO.Path.Combine(RuntimeFolder(root,environment),"python.zip");
            try
            {
                if(!File.Exists(zip)||Hash(zip)!=PythonHash)return false;
                string searchPaths=System.IO.Path.Combine(destination,"python311._pth");if(!File.Exists(searchPaths)||File.ReadAllText(searchPaths)!=PythonPaths)return false;
                using(var archive=new ZipArchive(File.OpenRead(zip),ZipArchiveMode.Read))
                {
                    foreach(string required in new[]{"python.exe","python311.dll","python311.zip"})if(archive.GetEntry(required)==null)return false;
                    foreach(var entry in archive.Entries)if(!string.IsNullOrEmpty(entry.Name)&&entry.FullName!="python311._pth"&&!RuntimeEntryMatches(entry,RuntimeEntryPath(destination,entry.FullName)))return false;
                }
                return true;
            }
            catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}catch(InvalidDataException){return false;}
        }
        static async Task PortablePython(string root,Action<string> progress,CancellationToken token,string environment="cuda")
        {
            if(!Environment.Is64BitOperatingSystem)throw new InvalidOperationException("本地 AI 需要 64 位 Windows。");
            string python=Python(root,environment),destination=System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(python)),zip=System.IO.Path.Combine(RuntimeFolder(root,environment),"python.zip");Directory.CreateDirectory(destination);
            string downloadName=environment=="cpu"?"runtime/cpu/python.zip":"runtime/python.zip";
            token.ThrowIfCancellationRequested();
            if(!File.Exists(zip)||Hash(zip)!=PythonHash)
            {
                string partial=zip+".part";progress(L.T("正在准备私有 Python 环境…"));ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                var watch=Stopwatch.StartNew();long priorBytes=0;double priorSeconds=0;long total=0;
                using(var web=new WebClient())using(token.Register(web.CancelAsync)){web.DownloadProgressChanged+=delegate(object sender,DownloadProgressChangedEventArgs args){double now=watch.Elapsed.TotalSeconds;total=args.TotalBytesToReceive;if(now-priorSeconds<.4&&args.BytesReceived<total)return;double speed=(args.BytesReceived-priorBytes)/Math.Max(.001,now-priorSeconds);priorBytes=args.BytesReceived;priorSeconds=now;progress(DownloadProgress(new Dictionary<string,object>{{"file",downloadName},{"bytes",args.BytesReceived},{"total",total},{"speed",speed},{"phase","downloading"}}));};try{await web.DownloadFileTaskAsync(new Uri(PythonUrl),partial);}catch(WebException){token.ThrowIfCancellationRequested();throw;}}
                token.ThrowIfCancellationRequested();progress(DownloadProgress(new Dictionary<string,object>{{"file",downloadName},{"bytes",new FileInfo(partial).Length},{"total",total},{"phase","verifying"}}));if(Hash(partial)!=PythonHash)throw new InvalidDataException("Python 下载校验失败。");
                if(File.Exists(zip))File.Replace(partial,zip,null);else File.Move(partial,zip);
                progress(DownloadProgress(new Dictionary<string,object>{{"file",downloadName},{"bytes",new FileInfo(zip).Length},{"total",new FileInfo(zip).Length},{"phase","verified"}}));
            }
            // A cancelled extraction may leave a valid exe but omit its DLL, stdlib
            // or extension modules. Verify every pinned archive entry on each repair.
            using(var archive=new ZipArchive(File.OpenRead(zip),ZipArchiveMode.Read))
            {
                foreach(string required in new[]{"python.exe","python311.dll","python311.zip"})if(archive.GetEntry(required)==null)throw new InvalidDataException("Python 运行环境不完整。");
                foreach(var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();string path=RuntimeEntryPath(destination,entry.FullName);if(string.IsNullOrEmpty(entry.Name)){Directory.CreateDirectory(path);continue;}
                    if(entry.FullName=="python311._pth"||RuntimeEntryMatches(entry,path))continue;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));using(var input=entry.Open())using(var bytes=new MemoryStream()){input.CopyTo(bytes);token.ThrowIfCancellationRequested();Codec.SaveAtomic(path,bytes.ToArray());}
                }
            }
            token.ThrowIfCancellationRequested();Codec.SaveAtomic(System.IO.Path.Combine(destination,"python311._pth"),Encoding.ASCII.GetBytes(PythonPaths));
        }
        public static async Task Prepare(QrAiOptions options,Action<string> progress,CancellationToken token,bool models=true)
        {
            QrAiStorage.EnsureStorage(options,models);
            ValidateScheme(options);QrAiStorage.RegisterEnvironment(options.Root,options.Environment);
            string worker=Materialize(options.Root);await PortablePython(options.Root,progress,token,options.Environment);
            var before=await Probe(options,progress,token);
            if(before.missing!=null&&before.missing.Length>0)await Run(Python(options.Root,options.Environment),worker,new Dictionary<string,object>{{"operation","bootstrap"},{"runtime",RuntimeFolder(options.Root,options.Environment)},{"source",options.Source},{"environment",options.Environment}},progress,null,token);
            var hardware=await Probe(options,progress,token);if(hardware.missing!=null&&hardware.missing.Length>0)throw new InvalidOperationException("AI 环境检查失败，请重新修复。");
            if(!models)return;
            var selectedBase=ReadModel(options.Base,false);var selectedControl=ReadModel(options.Control,true);
            if(selectedBase!=null&&selectedControl!=null&&selectedBase.Family!=selectedControl.Family)throw new InvalidOperationException("基础模型与二维码 ControlNet 的类型不匹配。");
            string family=selectedBase!=null?selectedBase.Family:selectedControl!=null?selectedControl.Family:options.Family;
            await VerifySelectedModels(options,progress,token);
            var pinned=PinnedSelections(options);
            await Run(Python(options.Root,options.Environment),worker,new Dictionary<string,object>{{"operation","download"},{"environment",options.Environment},{"models",options.Root},{"manifest",System.IO.Path.Combine(options.Root,"engine","models.json")},{"source",options.Source},{"family",family},{"search_roots",DownloadSearchRoots(options)},{"skip_weights",CustomWeightKinds(options)},{"force_hash",true}},progress,null,token);
            ResolveSelectedPinned(options,pinned);
        }
        public static async Task EnsureCaption(QrAiOptions options,Action<string> progress,CancellationToken token)
        {
            await Run(Python(options.Root,options.Environment),Materialize(options.Root),new Dictionary<string,object>{{"operation","download"},{"environment",options.Environment},{"models",options.Root},{"manifest",System.IO.Path.Combine(options.Root,"engine","models.json")},{"source",options.Source},{"family","shared"},{"search_roots",DownloadSearchRoots(options)}},progress,null,token);
        }
        public static Task<QrAiHardware> Probe(string root,Action<string> progress,CancellationToken token){return Probe(new QrAiOptions{Root=root},progress,token);}
        public static async Task<QrAiHardware> Probe(QrAiOptions options,Action<string> progress,CancellationToken token)
        {
            if(!PythonRuntimeComplete(options.Root,options.Environment))return new QrAiHardware{device="CPU",missing=new[]{"Python"}};
            var result=await Run(Python(options.Root,options.Environment),Materialize(options.Root),new Dictionary<string,object>{{"operation","probe"},{"environment",options.Environment}},progress,null,token);
            return Inspection.Json().Deserialize<QrAiHardware>(Inspection.Json().Serialize(result));
        }
        static List<QrAiModel> DiscoverLocal(string root)
        {
            var result=new List<QrAiModel>();foreach(bool control in new[]{false,true})
            {
                string start=System.IO.Path.Combine(root,control?"controlnet":"base");if(!Directory.Exists(start))continue;
                var pending=new Queue<Tuple<string,int>>();pending.Enqueue(Tuple.Create(start,0));
                while(pending.Count>0)
                {
                    var item=pending.Dequeue();try{if((File.GetAttributes(item.Item1)&FileAttributes.ReparsePoint)!=0)continue;}catch(IOException){continue;}catch(UnauthorizedAccessException){continue;}
                    string config=System.IO.Path.Combine(item.Item1,control?"config.json":"model_index.json");
                    try
                    {
                        if(File.Exists(config))
                        {
                            var model=ReadModel(item.Item1,control);if(model!=null)result.Add(model);
                            // A package with an invalid config must not be reinterpreted
                            // as a collection of standalone component checkpoints.
                            continue;
                        }
                        foreach(string file in Directory.GetFiles(item.Item1,"*.safetensors"))
                        {
                            if(!control&&new FileInfo(file).Length<100000000)continue;
                            var info=QrAiIntegrity.Inspect(file);if(info!=null&&info.Family!=null&&info.Role==(control?"control":"base"))result.Add(new QrAiModel{Path=file,Name=System.IO.Path.GetFileName(file),Family=info.Family,Control=control,Integrity="结构校验通过"});
                        }
                        if(item.Item2<2)foreach(string folder in Directory.GetDirectories(item.Item1))pending.Enqueue(Tuple.Create(folder,item.Item2+1));
                    }
                    catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
                }
            }
            return result.OrderBy(m=>m.Name,StringComparer.OrdinalIgnoreCase).ToList();
        }
        public static async Task<Dictionary<string,object>> Generate(QrAiOptions options,string input,string condition,string output,Action<string> progress,Action<string> caption,Func<string,bool> validate,CancellationToken token,string expected=null,bool rawPayload=false)
        {
            ValidateScheme(options);var hardware=await Probe(options,progress,token);
            if(hardware.missing!=null&&hardware.missing.Length>0)throw new InvalidOperationException("请先自动下载并修复环境。");
            var models=Discover(options.Root);var baseModel=models.FirstOrDefault(m=>!m.Control&&m.Path==options.Base);var controlModel=models.FirstOrDefault(m=>m.Control&&m.Path==options.Control);
            if(baseModel==null||controlModel==null)throw new InvalidOperationException("未找到配套模型，请先自动下载或重新扫描模型目录。");
            if(baseModel.Family!=controlModel.Family)throw new InvalidOperationException("基础模型与二维码 ControlNet 的类型不匹配。");
            await VerifySelectedModels(options,progress,token);
            var pinned=PinnedSelections(options);await Run(Python(options.Root,options.Environment),Materialize(options.Root),new Dictionary<string,object>{{"operation","download"},{"environment",options.Environment},{"models",options.Root},{"manifest",System.IO.Path.Combine(options.Root,"engine","models.json")},{"source",options.Source},{"family",baseModel.Family},{"skip_weights",CustomWeightKinds(options)},{"search_roots",DownloadSearchRoots(options)}},progress,null,token);ResolveSelectedPinned(options,pinned);
            string config=ResolveModelPackage(options.Root,"configs/"+baseModel.Family);if(!Directory.Exists(config)){var complete=models.FirstOrDefault(m=>!m.Control&&Directory.Exists(m.Path)&&m.Family==baseModel.Family);if(complete!=null)config=complete.Path;}
            var request=new Dictionary<string,object>{{"operation","generate"},{"environment",options.Environment},{"generation_profile",ProfileFor(options).Request()},{"models",options.Root},{"base",options.Base},{"control",options.Control},{"family",baseModel.Family},{"config",config},{"policy",options.Policy},{"input",input},{"condition",condition},{"output",output},{"auto_prompt",options.AutoPrompt},{"auto_background",options.AutoBackground},{"auto_place",options.AutoPlace},{"manual_skin",!options.AutoPlace},{"prompt",options.Prompt},{"negative",options.Negative},{"attempts",6},{"gradient_guidance",UseScanningGuidance(hardware,baseModel.Family,options.Policy)},{"natural_materials",true},{"material_blueprint",true},{"verify_decoder",true}};
            if(expected!=null){request.Add("expected",QrService.Payload(expected,rawPayload));request.Add("raw_payload",rawPayload);}
            return await Run(Python(options.Root,options.Environment),Materialize(options.Root),request,progress,delegate(Dictionary<string,object> message,Process process)
            {
                string kind=Value(message,"event");if(kind=="caption")caption(Value(message,"prompt"));
                if(kind=="candidate")
                {
                    bool okay=message.ContainsKey("decoder_ok")&&Convert.ToBoolean(message["decoder_ok"])&&validate(Value(message,"path"));
                    process.StandardInput.WriteLine(Inspection.Json().Serialize(new{accept=okay}));process.StandardInput.Flush();
                }
            },token);
        }
        public static bool UseScanningGuidance(QrAiHardware hardware,string family,string policy)
        {return hardware.cuda&&family=="sd15"&&policy!="low"&&hardware.total_gb>=16&&hardware.free_gb>=12;}
        public static Task<bool> ValidateOutput(string root,Bitmap image,string link,CancellationToken token,bool rawPayload=false){return ValidateOutput(new QrAiOptions{Root=root},image,link,token,rawPayload);}
        public static async Task<bool> ValidateOutput(QrAiOptions options,Bitmap image,string link,CancellationToken token,bool rawPayload=false)
        {
            string worker=Materialize(options.Root),folder=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(worker),"jobs");Directory.CreateDirectory(folder);
            string path=System.IO.Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");
            try
            {
                token.ThrowIfCancellationRequested();Codec.SaveAtomic(path,Codec.StaticPng(image));
                var result=await Run(Python(options.Root,options.Environment),worker,new Dictionary<string,object>{{"operation","validate"},{"environment",options.Environment},{"input",path},{"expected",QrService.Payload(link,rawPayload)},{"raw_payload",rawPayload}},delegate(string value){},null,token);
                object valid;return result.TryGetValue("valid",out valid)&&Convert.ToBoolean(valid);
            }
            finally{try{File.Delete(path);}catch(IOException){}}
        }
        static string Value(Dictionary<string,object> data,string name){object value;return data.TryGetValue(name,out value)?Convert.ToString(value):"";}
        public static bool MainlandRegion()
        {
            // Windows location setting, independent of the interface language.
            try{int geo=GetUserGeoID(16);var iso=new StringBuilder(8);return geo!=-1&&GetGeoInfoW(geo,4,iso,iso.Capacity,0)>0&&iso.ToString().Equals("CN",StringComparison.OrdinalIgnoreCase);}
            catch(DllNotFoundException){return false;}catch(EntryPointNotFoundException){return false;}
        }
        [DllImport("kernel32.dll")]static extern int GetUserGeoID(int geoClass);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetGeoInfoW(int geoId,int geoType,StringBuilder data,int count,int language);
        static double Number(Dictionary<string,object> data,string key){object value;double result;return data.TryGetValue(key,out value)&&value!=null&&double.TryParse(Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out result)&&!double.IsNaN(result)&&!double.IsInfinity(result)?Math.Max(0,result):0;}
        static string SizeText(double bytes){string[] units={"B","KiB","MiB","GiB","TiB"};int index=0;while(bytes>=1024&&index<units.Length-1){bytes/=1024;index++;}return bytes.ToString(index==0?"0":"0.0",System.Globalization.CultureInfo.InvariantCulture)+" "+units[index];}
        public static string DownloadProgress(Dictionary<string,object> data)
        {
            double bytes=Number(data,"bytes"),total=Number(data,"total"),speed=Number(data,"speed");string phase=Value(data,"phase"),state;
            if(phase=="cached")state=L.T("缓存已校验");else if(phase=="verifying")state=L.T("校验中");else if(phase=="verified")state=L.T("已校验完成");else if(phase=="invalid")state=L.T("校验失败");else if(phase=="retry")state=L.T("等待重试");else if(phase=="connecting")state=L.T("连接中");else state=total>0?Math.Min(99.9,bytes*100/total).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"%":L.T("总大小未知");
            return Value(data,"file")+" · "+SizeText(bytes)+" / "+(total>0?SizeText(total):L.T("未知"))+" · "+SizeText(speed)+"/s · "+state;
        }
        public static string OverallProgress(Dictionary<string,object> data)
        {
            double done=Number(data,"completed_files"),count=Number(data,"total_files"),bytes=Number(data,"downloaded_bytes"),total=Number(data,"total_bytes");object ready;bool complete=data.TryGetValue("ready",out ready)&&ready is bool&&(bool)ready&&done==count;string phase=Value(data,"phase");
            string state=complete&&phase=="ready"?L.T("全部校验通过"):phase=="failed"?L.T("未完成，发生错误"):phase=="incomplete"?L.T("仍有缺失文件"):L.T("下载 / 校验中");
            return L.T("模型文件完成数：")+done.ToString("0")+" / "+count.ToString("0")+" · "+L.T("整体进度：")+(total>0?(complete&&phase=="ready"?100:Math.Min(99.9,bytes*100/total)).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"%":L.T("计算中"))+" · "+SizeText(bytes)+" / "+SizeText(total)+" · "+state;
        }
        static string NetworkProgress(Dictionary<string,object> data)
        {
            string chosen=Value(data,"source")=="mirror"?L.T("中国镜像源"):L.T("官方源");string reason=Value(data,"reason")=="region"?L.T("大陆地区优先镜像"):Value(data,"reason")=="fallback"?L.T("探测未通过，将尝试备用源"):L.T("按可达性与延迟选择");
            var endpoints=new List<string>();object list;
            if(data.TryGetValue("endpoints",out list)&&list is System.Collections.IEnumerable)foreach(object item in (System.Collections.IEnumerable)list){var probe=item as Dictionary<string,object>;if(probe==null)continue;object reachable;bool okay=probe.TryGetValue("reachable",out reachable)&&Convert.ToBoolean(reachable);endpoints.Add((Value(probe,"source")=="mirror"?L.T("中国镜像源"):L.T("官方源"))+": "+(okay?Number(probe,"latency_ms").ToString("0",System.Globalization.CultureInfo.InvariantCulture)+" ms":L.T("不可达")));}
            return L.T("自动选择下载源：")+chosen+" · "+reason+(endpoints.Count>0?"\n"+string.Join(" · ",endpoints):"");
        }
        public static async Task<Dictionary<string,object>> Run(string python,string worker,Dictionary<string,object> request,Action<string> progress,Action<Dictionary<string,object>,Process> received,CancellationToken token)
        {
            string folder=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(worker),"jobs");Directory.CreateDirectory(folder);string path=System.IO.Path.Combine(folder,Guid.NewGuid().ToString("N")+".json");
            try
            {
                token.ThrowIfCancellationRequested();if((Value(request,"operation")=="download"||Value(request,"operation")=="bootstrap")&&!request.ContainsKey("prefer_mirror"))request["prefer_mirror"]=MainlandRegion();File.WriteAllText(path,Inspection.Json().Serialize(request),new UTF8Encoding(false));
                return await Task.Run(()=>
            {
                token.ThrowIfCancellationRequested();var result=new Dictionary<string,object>();string error=null,errorCode=null,overall=null,detail=null;var diagnostics=new StringBuilder();var activeDownloads=new Dictionary<string,Dictionary<string,object>>(StringComparer.Ordinal);
                using(var process=new Process())using(var lifetime=new InferenceLifetime())
                {
                    process.StartInfo=new ProcessStartInfo(python,"-X utf8 "+Quote(worker)+" "+Quote(path)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=System.IO.Path.GetDirectoryName(worker)};
                    process.StartInfo.EnvironmentVariables["PYTHONUTF8"]="1";process.StartInfo.EnvironmentVariables["HF_HUB_DISABLE_TELEMETRY"]="1";
                    string runtime=request.ContainsKey("runtime")?Path.GetFullPath(Value(request,"runtime")):Path.GetFullPath(Path.Combine(Path.GetDirectoryName(worker),"..","runtime",Value(request,"environment")=="cpu"?"cpu":""));foreach(string child in new[]{"temp","pip-cache","hf-cache","torch-cache"})Directory.CreateDirectory(Path.Combine(runtime,child));
                    process.StartInfo.EnvironmentVariables["TEMP"]=process.StartInfo.EnvironmentVariables["TMP"]=Path.Combine(runtime,"temp");process.StartInfo.EnvironmentVariables["PIP_CACHE_DIR"]=Path.Combine(runtime,"pip-cache");process.StartInfo.EnvironmentVariables["HF_HOME"]=Path.Combine(runtime,"hf-cache");process.StartInfo.EnvironmentVariables["TORCH_HOME"]=Path.Combine(runtime,"torch-cache");
                    process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs args){if(args.Data!=null)lock(diagnostics){if(diagnostics.Length>12000)diagnostics.Remove(0,6000);diagnostics.AppendLine(args.Data);}};
                    process.Start();lifetime.Attach(process);process.BeginErrorReadLine();
                    using(token.Register(delegate{lifetime.Stop();try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}}))
                    {
                        string line;while((line=process.StandardOutput.ReadLine())!=null)
                        {
                            token.ThrowIfCancellationRequested();Dictionary<string,object> data;try{data=Inspection.Json().Deserialize<Dictionary<string,object>>(line);}catch(ArgumentException){continue;}if(data==null)continue;
                            string kind=Value(data,"event");if(kind=="result")result=data;if(kind=="error"){error=Value(data,"message");errorCode=Value(data,"code");}
                            if(kind=="progress"||kind=="released"||kind=="error")progress(Value(data,"message"));
                            if(kind=="network")progress(NetworkProgress(data));
                            if(kind=="overall"){overall=OverallProgress(data);progress(overall+(detail==null?"":"\n"+detail));}
                            if(kind=="environment"){string phase=Value(data,"phase");progress(L.T("AI 环境安装阶段：")+L.T(phase=="preparing"?"准备中":phase=="installing"?"安装依赖中":phase=="verifying"?"校验中":"已校验完成")+" · "+L.T("安装包总大小由依赖解析后确定；模型总进度将在环境就绪后显示。"));}
                            if(kind=="download"||kind=="verification"){string file=Value(data,"file"),phase=Value(data,"phase");bool finished=phase=="verified"||phase=="cached"||phase=="invalid";if(finished)activeDownloads.Remove(file);else activeDownloads[file]=data;var lines=activeDownloads.Values.Select(DownloadProgress).Take(3).ToList();if(lines.Count==0||finished)lines.Insert(0,DownloadProgress(data));detail=L.T(kind=="verification"?"校验模型文件：":"下载文件：")+"\n"+string.Join("\n",lines.Take(3));progress((overall==null?"":overall+"\n")+detail);}
                            if(kind=="step")progress(L.T("AI 生图进度：")+Value(data,"attempt")+" · "+Value(data,"step")+" / "+Value(data,"total"));
                            if(received!=null)received(data,process);
                        }
                        process.WaitForExit();token.ThrowIfCancellationRequested();if(process.ExitCode!=0){if(Value(request,"operation")=="generate"&&errorCode=="scan_failed")throw new QrAiScanException(error??"手动位置未通过扫码。");throw new InvalidOperationException(error??diagnostics.ToString());}
                    }
                }
                string operation=Value(request,"operation");if(result.Count==0)throw new InvalidOperationException("AI 任务未返回完成结果，请重试。");object inspect;bool inspection=request.TryGetValue("inspection_only",out inspect)&&inspect is bool&&(bool)inspect;if(operation=="download"&&!inspection){object ready;if(!result.TryGetValue("ready",out ready)||!(ready is bool)||!(bool)ready)throw new InvalidOperationException("模型下载尚未全部完成或校验失败，请继续下载 / 修复。");}
                return result;
                },token);
            }
            finally{try{File.Delete(path);}catch(IOException){}}
        }
        static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
        sealed class InferenceLifetime:IDisposable
        {
            IntPtr job=CreateJobObject(IntPtr.Zero,null);
            public InferenceLifetime(){if(job==IntPtr.Zero)throw new InvalidOperationException("无法建立 AI 资源管理组。");var info=new JobExtended();info.Basic.LimitFlags=0x2000;int n=Marshal.SizeOf(info);IntPtr memory=Marshal.AllocHGlobal(n);try{Marshal.StructureToPtr(info,memory,false);if(!SetInformationJobObject(job,9,memory,(uint)n))throw new InvalidOperationException("无法配置 AI 资源管理组。");}finally{Marshal.FreeHGlobal(memory);}}
            public void Attach(Process process){if(!AssignProcessToJobObject(job,process.Handle)){try{process.Kill();}catch(InvalidOperationException){}throw new InvalidOperationException("无法隔离 AI 推理进程。");}}
            public void Stop(){if(job!=IntPtr.Zero)TerminateJobObject(job,1);}
            public void Dispose(){if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}}
            [StructLayout(LayoutKind.Sequential)]struct JobBasic{public long ProcessTime,JobTime;public uint LimitFlags;public UIntPtr MinWorkingSet,MaxWorkingSet;public uint ActiveProcesses;public UIntPtr Affinity;public uint Priority,Scheduling;}
            [StructLayout(LayoutKind.Sequential)]struct IoCounters{public ulong Read,Write,Other,ReadBytes,WriteBytes,OtherBytes;}
            [StructLayout(LayoutKind.Sequential)]struct JobExtended{public JobBasic Basic;public IoCounters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
            [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateJobObject(IntPtr attributes,string name);
            [DllImport("kernel32.dll")]static extern bool SetInformationJobObject(IntPtr job,int kind,IntPtr data,uint length);
            [DllImport("kernel32.dll")]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
            [DllImport("kernel32.dll")]static extern bool TerminateJobObject(IntPtr job,uint code);
            [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
        }
    }
}
