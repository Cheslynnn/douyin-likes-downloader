using System; using System.IO; using System.Net; using System.Net.WebSockets; using System.Net.Sockets; using System.Text; using System.Drawing; using System.Diagnostics; using System.Threading; using System.Threading.Tasks; using System.Windows.Forms; using System.Collections.Generic; using System.Web.Script.Serialization; using System.Reflection;

sealed class Cdp : IDisposable {
  ClientWebSocket socket=new ClientWebSocket(); int sequence; JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=50*1024*1024};
  public async Task Connect(string url){socket.Options.Proxy=null;await socket.ConnectAsync(new Uri(url),CancellationToken.None);}
  public async Task<Dictionary<string,object>> Call(string method,object args) {
    int id=++sequence; var msg=json.Serialize(new {id=id,method=method,@params=args}); var bytes=Encoding.UTF8.GetBytes(msg);
    await socket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,CancellationToken.None);
    while(true){using(var m=new MemoryStream()){var buf=new byte[16384];WebSocketReceiveResult r;do{r=await socket.ReceiveAsync(new ArraySegment<byte>(buf),CancellationToken.None);if(r.MessageType==WebSocketMessageType.Close)throw new Exception("抖音窗口已关闭");m.Write(buf,0,r.Count);if(m.Length>50*1024*1024)throw new Exception("网页返回的数据过大");}while(!r.EndOfMessage); var d=json.DeserializeObject(Encoding.UTF8.GetString(m.ToArray())) as Dictionary<string,object>;object n;if(d!=null&&d.TryGetValue("id",out n)&&Convert.ToInt32(n)==id){object err;if(d.TryGetValue("error",out err))throw new Exception("浏览器操作失败："+json.Serialize(err));return d;}}}
  }
  public async Task<object> Eval(string expression){var d=await Call("Runtime.evaluate",new{expression=expression,returnByValue=true,awaitPromise=false});var r=(Dictionary<string,object>)d["result"];object err;if(r.TryGetValue("exceptionDetails",out err))throw new Exception("网页脚本执行失败，请确认已打开抖音个人主页");var val=(Dictionary<string,object>)r["result"];object v;return val.TryGetValue("value",out v)?v:null;}
  public void Dispose(){socket.Dispose();}
}

sealed class MainForm:Form {
  TextBox output,log; Button open,start,import; Label status; bool running; JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=50*1024*1024};
  public MainForm(){Text="抖音喜欢下载";StartPosition=FormStartPosition.CenterScreen;Size=new Size(860,690);MinimumSize=new Size(760,610);Font=new Font("Microsoft YaHei UI",10);BackColor=Color.FromArgb(248,249,251);ForeColor=Color.FromArgb(34,39,48);
    var title=new Label{Text="抖音喜欢下载",Font=new Font("Microsoft YaHei UI",20,FontStyle.Bold),Left=32,Top=24,Width=500,Height=45};Controls.Add(title);
    var subtitle=new Label{Text="打开喜欢页面，一键采集并保存到电脑",ForeColor=Color.FromArgb(108,117,131),Left=34,Top=70,Width=700,Height=28};Controls.Add(subtitle);
    var line=new Panel{Left=32,Top=112,Width=780,Height=1,BackColor=Color.FromArgb(227,230,235),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};Controls.Add(line);
    var step=new Label{Text="① 登录抖音",Font=new Font("Microsoft YaHei UI",11,FontStyle.Bold),Left=34,Top=138,Width=250,Height=26};Controls.Add(step);
    var hint=new Label{Text="在专用窗口登录，打开自己的个人主页并进入“喜欢”。",ForeColor=Color.FromArgb(108,117,131),Left=34,Top=170,Width=580,Height=26};Controls.Add(hint);
    open=MakeButton("打开抖音窗口",false);open.SetBounds(630,142,172,42);open.Anchor=AnchorStyles.Top|AnchorStyles.Right;open.Click+=OpenBrowser;Controls.Add(open);
    Controls.Add(new Label{Text="② 选择保存位置",Font=new Font("Microsoft YaHei UI",11,FontStyle.Bold),Left=34,Top=216,Width=250,Height=28});
    output=new TextBox{Left=34,Top=251,Width=594,Height=30,Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"抖音下载结果"),Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,BorderStyle=BorderStyle.FixedSingle};Controls.Add(output);
    var folder=MakeButton("选择文件夹",false);folder.SetBounds(644,246,158,38);folder.Anchor=AnchorStyles.Top|AnchorStyles.Right;folder.Click+=(s,e)=>{using(var d=new FolderBrowserDialog())if(d.ShowDialog()==DialogResult.OK)output.Text=d.SelectedPath;};Controls.Add(folder);
    start=MakeButton("一键采集并下载",true);start.SetBounds(34,310,200,46);start.Click+=StartCollect;Controls.Add(start);
    import=MakeButton("导入已有清单",false);import.SetBounds(248,310,165,46);import.Click+=Import;Controls.Add(import);
    status=new Label{Text="准备就绪",ForeColor=Color.FromArgb(104,113,128),Left=430,Top=322,Width=372,Height=28,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};Controls.Add(status);
    Controls.Add(new Label{Text="任务进度",Font=new Font("Microsoft YaHei UI",11,FontStyle.Bold),Left=34,Top=392,Width=220,Height=28});
    log=new TextBox{Left=34,Top=430,Width=768,Height=175,Multiline=true,ReadOnly=true,BackColor=Color.White,ForeColor=Color.FromArgb(71,81,98),BorderStyle=BorderStyle.FixedSingle,ScrollBars=ScrollBars.Vertical,Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right};Controls.Add(log);
    FormClosing+=(s,e)=>{if(running&&MessageBox.Show("任务正在进行，关闭会中断采集或下载。确定关闭吗？","确认",MessageBoxButtons.YesNo)!=DialogResult.Yes)e.Cancel=true;}; }
  Button MakeButton(string text,bool primary){var b=new Button{Text=text,FlatStyle=FlatStyle.Flat,BackColor=primary?Color.FromArgb(45,51,62):Color.White,ForeColor=primary?Color.White:Color.FromArgb(42,49,60),Cursor=Cursors.Hand,UseVisualStyleBackColor=false};b.FlatAppearance.BorderSize=primary?0:1;b.FlatAppearance.BorderColor=Color.FromArgb(218,222,229);return b;}
  void Log(string text){if(IsDisposed)return;if(InvokeRequired){try{BeginInvoke((Action)(()=>Log(text)));}catch{}return;}log.AppendText(text+Environment.NewLine);status.Text=text;}
  void SetRunning(bool yes){running=yes;start.Enabled=!yes;import.Enabled=!yes;open.Enabled=!yes;}
  static string BrowserPath(){string p=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google\\Chrome\\Application\\chrome.exe");if(File.Exists(p))return p;p=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft\\Edge\\Application\\msedge.exe");if(File.Exists(p))return p;throw new Exception("没有找到 Chrome 或 Microsoft Edge 浏览器。");}
  static string ProfilePath(){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DouyinCollector","BrowserProfile");}
  void OpenBrowser(object sender,EventArgs e){try{var profile=ProfilePath();Directory.CreateDirectory(profile);var args="--remote-debugging-port=0 --remote-debugging-address=127.0.0.1 --user-data-dir=\""+profile+"\" --no-first-run https://www.douyin.com/";Process.Start(new ProcessStartInfo(BrowserPath(),args){UseShellExecute=false});Log("请在新窗口登录并打开自己的喜欢页面，然后点“一键采集并下载”。");}catch(Exception ex){MessageBox.Show(ex.Message,"无法打开浏览器");}}
  object ReadJson(string url){var req=(HttpWebRequest)WebRequest.Create(url);req.Proxy=null;req.Timeout=5000;using(var response=req.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))return json.DeserializeObject(reader.ReadToEnd());}
  string FindTab(){int port;string portFile=Path.Combine(ProfilePath(),"DevToolsActivePort");string[] lines=File.Exists(portFile)?File.ReadAllLines(portFile):new string[0];if(lines.Length==0||!Int32.TryParse(lines[0],out port)||port<1||port>65535)throw new Exception("请先点击“打开抖音窗口”，登录并打开自己的喜欢页面。");var list=ReadJson("http://127.0.0.1:"+port+"/json/list") as object[];if(list!=null)foreach(var x in list){var d=x as Dictionary<string,object>;if(Core.Get(d,"type")!="page")continue;Uri u;if(!Uri.TryCreate(Core.Get(d,"url"),UriKind.Absolute,out u))continue;if((u.Host=="douyin.com"||u.Host.EndsWith(".douyin.com"))&&u.AbsolutePath.StartsWith("/user/"))return Core.Get(d,"webSocketDebuggerUrl");}throw new Exception("没有找到个人主页。请用软件打开抖音窗口，登录后进入自己的主页及喜欢页面。");}
  string Collector(){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("collector.js"))using(var r=new StreamReader(stream,Encoding.UTF8))return r.ReadToEnd();}
  async void StartCollect(object sender,EventArgs e){if(running)return;string dir=output.Text.Trim();try{if(String.IsNullOrWhiteSpace(dir))throw new Exception("请选择保存位置");Directory.CreateDirectory(dir);}catch(Exception ex){MessageBox.Show(ex.Message,"无法开始");return;}SetRunning(true);log.Clear();try{Log("正在连接抖音窗口…");string ws=await Task.Run(()=>FindTab());using(var c=new Cdp()){await c.Connect(ws);await c.Eval(Collector());Log("正在准备喜欢列表，请保持抖音窗口打开。");DateTime last=DateTime.UtcNow;int previous=-1;bool started=false;
    while(true){await Task.Delay(1500);var st=await c.Eval("window.__douyinLikesExport && window.__douyinLikesExport.status()") as Dictionary<string,object>;if(st==null)throw new Exception("页面已变化，请重新打开自己的喜欢页面后重试。");int count=Convert.ToInt32(st["items"]),pages=Convert.ToInt32(st["pages"]);bool complete=Convert.ToBoolean(st["complete"]);if(count!=previous){previous=count;last=DateTime.UtcNow;Log("已采集 "+count+" 条，"+pages+" 页。");}if(pages>0&&!started&&!complete){await c.Eval("window.__douyinLikesExport.startAuto(); true");started=true;}if(complete){Log("已连续采集到末页，正在获取图文原图地址。");break;}if((DateTime.UtcNow-last).TotalSeconds>65)throw new Exception("列表未继续加载，尚未确认完整。请检查抖音登录、验证或喜欢页面后重试。");}
    await c.Eval("window.__unifiedResult=null;window.__unifiedError=null;window.__douyinLikesExport.manifest().then(x=>window.__unifiedResult=x).catch(e=>window.__unifiedError=String(e));true");int lastDone=-1;
    while(true){await Task.Delay(1000);var state=await c.Eval("({done:!!window.__unifiedResult,error:window.__unifiedError,progress:window.__douyinProgress||null})") as Dictionary<string,object>;object error;state.TryGetValue("error",out error);if(error!=null)throw new Exception("获取图文详情失败："+error);if(Convert.ToBoolean(state["done"]))break;object progress;if(state.TryGetValue("progress",out progress)&&progress is Dictionary<string,object>){var p=(Dictionary<string,object>)progress;int n=Convert.ToInt32(p["done"]);if(n!=lastDone){lastDone=n;Log("正在解析作品 "+n+" / "+p["total"]+"，图文失败 "+p["errors"]+" 条。");}}}
    var manifest=await c.Eval("window.__unifiedResult") as Dictionary<string,object>;if(manifest==null)throw new Exception("没有收到采集结果");Log("采集完成，开始保存文件。");await Task.Run(()=>Run(manifest,dir));}}
    catch(Exception ex){Log("任务未完成："+ex.Message);}finally{SetRunning(false);}}
  async void Import(object sender,EventArgs e){if(running)return;using(var d=new OpenFileDialog{Filter="JSON 清单 (*.json)|*.json"}){if(d.ShowDialog()!=DialogResult.OK)return;try{var f=new FileInfo(d.FileName);if(f.Length>50*1024*1024)throw new Exception("清单超过 50 MB");var m=Core.Parse(File.ReadAllText(d.FileName,Encoding.UTF8));string dir=output.Text.Trim();if(String.IsNullOrWhiteSpace(dir))throw new Exception("请选择保存位置");Directory.CreateDirectory(dir);SetRunning(true);log.Clear();await Task.Run(()=>Run(m,dir));}catch(Exception ex){Log("无法完成："+ex.Message);}finally{SetRunning(false);}}}
  static int WatermarkScore(string u) { return (u ?? "").IndexOf("watermark",StringComparison.OrdinalIgnoreCase)>=0 ? 1 : 0; }
  void Run(Dictionary<string,object> manifest,string root){
    ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
    string videoDir=Path.Combine(root,"视频"), imageDir=Path.Combine(root,"图片");
    Directory.CreateDirectory(videoDir); Directory.CreateDirectory(imageDir);
    var items=(object[])manifest["items"]; var report=new StringBuilder();
    report.AppendLine("抖音清单下载报告"); report.AppendLine("清单完整状态："+Core.Get(manifest,"complete"));
    var usedVideos=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var usedImages=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach(string f in Directory.EnumerateFiles(videoDir,"*.mp4"))usedVideos.Add(Path.GetFileNameWithoutExtension(f));
    foreach(string f in Directory.EnumerateFiles(imageDir,"*.jpg")){var n=Path.GetFileNameWithoutExtension(f);var match=System.Text.RegularExpressions.Regex.Match(n,@"^(.*)_\d+$");if(match.Success)usedImages.Add(match.Groups[1].Value);}
    var seen=new HashSet<string>(); int done=0,failed=0,skip=0;
    for(int i=0;i<items.Length;i++){
      var item=items[i] as Dictionary<string,object>; string id=Core.Get(item,"id"), title=Core.Get(item,"title");
      if(!System.Text.RegularExpressions.Regex.IsMatch(id,@"^\d{5,30}$")||!seen.Add(id)){skip++;report.AppendLine("跳过：无效或重复 ID "+id);continue;}
      try {
        object value; var assets=item.TryGetValue("assets",out value)?value as object[]:null;
        if(assets==null||assets.Length==0){failed++;report.AppendLine(id+"：清单没有媒体地址");Log((i+1)+"/"+items.Length+"："+id+" 没有媒体地址");continue;}
        int imageIndex=0;
        string baseName=Core.Clean(title), chosen=baseName; int collision=1;
        bool hasImage=false,hasVideo=false;
        foreach(var candidate in assets){var d=candidate as Dictionary<string,object>; if(Core.Get(d,"kind")=="image")hasImage=true; if(Core.Get(d,"kind")=="video")hasVideo=true;}
        while ((hasVideo && usedVideos.Contains(chosen)) || (hasImage && usedImages.Contains(chosen))) { collision++; chosen=baseName+" ("+collision+")"; }
        if(hasImage)usedImages.Add(chosen); if(hasVideo)usedVideos.Add(chosen);
        for(int j=0;j<assets.Length;j++){
          var asset=assets[j] as Dictionary<string,object>; string kind=Core.Get(asset,"kind");
          if(kind!="image" && kind!="video"){failed++;report.AppendLine(id+"：不支持的媒体类型 "+kind);continue;}
          bool image=kind=="image"; if(image)imageIndex++;
          string stem=chosen+(image?"_"+imageIndex.ToString("D2"):"");
          object u; var urls=asset!=null&&asset.TryGetValue("urls",out u)?u as object[]:null;
          string result="没有可用地址"; bool ok=false;
          if(urls!=null) {
            var candidates=new List<string>(); foreach(var v in urls) candidates.Add(Convert.ToString(v));
            if(image) { var ordered=candidates.FindAll(x=>WatermarkScore(x)==0); ordered.AddRange(candidates.FindAll(x=>WatermarkScore(x)!=0)); candidates=ordered; }
            foreach(var v in candidates){try{result=Core.Download(v,image?imageDir:videoDir,stem,image);ok=true;break;}catch(Exception ex){result=ex.Message;}}
          }
          if(ok)done++;else failed++; string line=id+" 第 "+(j+1)+" 项："+result;report.AppendLine(line);Log((i+1)+"/"+items.Length+" "+line);
        }
      }catch(Exception ex){failed++;report.AppendLine(id+"："+ex.Message);Log(id+"："+ex.Message);}
    }
    string final="完成：成功或已存在 "+done+" 项，失败 "+failed+" 项，跳过 "+skip+" 条";
    report.AppendLine(final);string reportPath=Path.Combine(root,"下载报告_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".txt");
    File.WriteAllText(reportPath,report.ToString(),new UTF8Encoding(true));Log(final+"。报告已保存。");
  }
}
