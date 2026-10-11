using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace QQImageSwitch
{
    static class QrAiProgressTests
    {
        static void Require(bool okay,string message){if(!okay)throw new Exception(message);}
        public static void Run(string root)
        {
            var progress=new Dictionary<string,object>{{"completed_files",2},{"total_files",3},{"downloaded_bytes",100},{"total_bytes",100},{"phase","verifying"},{"ready",false}};
            string value=QrAiService.OverallProgress(progress);Require(value.Contains("2 / 3")&&value.Contains("99.9%")&&!value.Contains(L.T("全部校验通过")),"received bytes incorrectly count as verified completion");
            progress["completed_files"]=3;progress["phase"]="ready";progress["ready"]=true;value=QrAiService.OverallProgress(progress);Require(value.Contains("100.0%")&&value.Contains(L.T("全部校验通过")),"verified final total missing");
            progress["phase"]="failed";progress["ready"]=false;Require(!QrAiService.OverallProgress(progress).Contains("100.0%"),"failed batch displayed 100%");
            string python=Environment.GetEnvironmentVariable("AIPT_TEST_PYTHON");if(string.IsNullOrEmpty(python)||!File.Exists(python))return;
            string dir=Path.Combine(root,"ai-progress-tests","engine");Directory.CreateDirectory(dir);string worker=Path.Combine(dir,"fake-worker.py");
            foreach(var scenario in new[]{"missing-result","false-ready","false-inspection","exit-failure","true-ready","inspection"})
            {
                string output=scenario=="missing-result"?"print('{\"event\":\"progress\",\"message\":\"done\"}')":scenario=="exit-failure"?"print('{\"event\":\"result\",\"ready\":true}'); raise SystemExit(1)":"print('{\"event\":\"result\",\"ready\":"+(scenario=="true-ready"?"true":"false")+"}')";
                File.WriteAllText(worker,output);var request=new Dictionary<string,object>{{"operation","download"}};if(scenario=="false-inspection")request["inspection_only"]=false;if(scenario=="inspection")request["inspection_only"]=true;bool rejected=false;
                try{Task.Run(async ()=>await QrAiService.Run(python,worker,request,s=>{},null,CancellationToken.None)).GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}
                Require(rejected!=(scenario=="true-ready"||scenario=="inspection"),"worker completion gate incorrect: "+scenario);
            }
            foreach(var scenario in new[]{"scan_failed","failed","no-code","scan-on-download"})
            {
                File.WriteAllText(worker,"print('{\"event\":\"error\",\"message\":\"fixture failed\",\"code\":\""+(scenario=="scan-on-download"?"scan_failed":scenario)+"\"}'); raise SystemExit(1)");
                var request=new Dictionary<string,object>{{"operation",scenario=="scan-on-download"?"download":"generate"}};Exception failure=null;try{Task.Run(async ()=>await QrAiService.Run(python,worker,request,s=>{},null,CancellationToken.None)).GetAwaiter().GetResult();}catch(InvalidOperationException ex){failure=ex;}
                Require(failure!=null&&(failure is QrAiScanException)==(scenario=="scan_failed"),"automatic-avoidance prompt offered for non-scan failure: "+scenario);
            }
            Require(Directory.GetFiles(Path.Combine(dir,"jobs")).Length==0,"worker request files leaked");File.WriteAllText(Path.Combine(root,"ai-progress-tests","PASS.md"),"PASS: truthful overall totals; missing result, false readiness, false inspection flag and nonzero exit are rejected; true ready / actual inspection accepted; request files cleaned.");
        }
    }
}
