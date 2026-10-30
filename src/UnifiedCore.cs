using System;
using System.IO;
using System.Net;
using System.Text;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Security.Cryptography;

static class Program {
  [STAThread] static void Main(string[] args) {
    if (args.Length == 1 && args[0] == "--selftest") { Environment.Exit(SelfTest()); return; }
    if (args.Length == 3 && args[0] == "--test-image") { try { Core.ConvertToJpeg(args[1],args[2]); Environment.Exit(0); } catch { Environment.Exit(1); } return; }
    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm());
  }
  static int SelfTest() {
    try { if (Core.Clean("a/b:c") != "a b c") return 1; if (!Core.Allowed("https://www.douyin.com/a") || Core.Allowed("http://www.douyin.com/a") || Core.Allowed("https://douyin.com.evil.test/a")) return 2; if (Core.Detect(new byte[]{0x89,0x50,0x4e,0x47,13,10,26,10}) != ".png") return 3; var m=Core.Parse("{\"schema\":\"douyin-media-manifest/v1\",\"items\":[]}"); if (!m.ContainsKey("items")) return 4; return 0; } catch { return 9; }
  }
}
static class Core {
  public static Dictionary<string,object> Parse(string text) { var j=new JavaScriptSerializer(); j.MaxJsonLength=50*1024*1024; var d=j.DeserializeObject(text) as Dictionary<string,object>; if(d==null || Get(d,"schema")!="douyin-media-manifest/v1" || !(d.ContainsKey("items") && d["items"] is object[])) throw new Exception("请选择采集脚本导出的下载清单 JSON。"); return d; }
  public static string Get(Dictionary<string,object> d,string key){ object x; return d!=null && d.TryGetValue(key,out x) && x!=null ? Convert.ToString(x) : ""; }
  public static string Clean(string s) { var b=new StringBuilder(); foreach(char c in s ?? "") b.Append(c<32 || "<>:\"/\\|?*".IndexOf(c)>=0 ? ' ' : c); string r=System.Text.RegularExpressions.Regex.Replace(b.ToString(),"\\s+"," ").Trim().TrimEnd('.',' '); if(r.Length>60) r=r.Substring(0,60).TrimEnd('.',' '); if(r.Length==0 || System.Text.RegularExpressions.Regex.IsMatch(r,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)) r="无文案"; return r; }
  public static bool Allowed(string value) { Uri u; if(!Uri.TryCreate(value,UriKind.Absolute,out u)||u.Scheme!="https"||!u.IsDefaultPort||u.UserInfo!="") return false; string h=u.DnsSafeHost.ToLowerInvariant(); foreach(var d in new[]{"douyin.com","douyinpic.com","douyinvod.com","byteimg.com","bytedance.com","bytedancecdn.com","ibytedtos.com","snssdk.com","ixigua.com","toutiao.com","pstatp.com"}) if(h==d || h.EndsWith("."+d,StringComparison.Ordinal)) return true; return false; }
  public static string Detect(byte[] b) { if(b.Length>=8 && b[0]==0x89 && b[1]==0x50 && b[2]==0x4e && b[3]==0x47) return ".png"; if(b.Length>=3 && b[0]==0xff && b[1]==0xd8 && b[2]==0xff) return ".jpg"; if(b.Length>=12 && Encoding.ASCII.GetString(b,0,4)=="RIFF" && Encoding.ASCII.GetString(b,8,4)=="WEBP") return ".webp"; if(b.Length>=6 && Encoding.ASCII.GetString(b,0,3)=="GIF") return ".gif"; return ".img"; }
  public static HttpWebResponse Open(string address) { for(int i=0;i<6;i++){ if(!Allowed(address)) throw new Exception("媒体地址域名不受支持"); var req=(HttpWebRequest)WebRequest.Create(address); req.AllowAutoRedirect=false; req.UserAgent="Mozilla/5.0"; req.Referer="https://www.douyin.com/"; req.Timeout=30000; req.ReadWriteTimeout=30000; var r=(HttpWebResponse)req.GetResponse(); int code=(int)r.StatusCode; if(code>=300 && code<400){ string loc=r.Headers["Location"]; r.Dispose(); if(String.IsNullOrEmpty(loc)) throw new Exception("重定向地址缺失"); address=new Uri(new Uri(address),loc).AbsoluteUri; continue; } if(code!=200){r.Dispose();throw new Exception("HTTP "+code);} if((r.ContentType??"").ToLowerInvariant().Contains("text/html") || (r.ContentType??"").ToLowerInvariant().Contains("application/json")){r.Dispose();throw new Exception("服务器返回了网页或错误信息");} return r; } throw new Exception("重定向过多"); }
  public static void ConvertToJpeg(string input,string output) {
    using(var f=File.OpenRead(input)) {
      var decoder=System.Windows.Media.Imaging.BitmapDecoder.Create(f,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
      var frame=decoder.Frames[0]; int w=frame.PixelWidth,h=frame.PixelHeight;
      var visual=new System.Windows.Media.DrawingVisual();
      using(var dc=visual.RenderOpen()) { dc.DrawRectangle(System.Windows.Media.Brushes.White,null,new System.Windows.Rect(0,0,w,h)); dc.DrawImage(frame,new System.Windows.Rect(0,0,w,h)); }
      var target=new System.Windows.Media.Imaging.RenderTargetBitmap(w,h,96,96,System.Windows.Media.PixelFormats.Pbgra32); target.Render(visual);
      var encoder=new System.Windows.Media.Imaging.JpegBitmapEncoder{QualityLevel=95}; encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
      using(var o=new FileStream(output,FileMode.CreateNew)) encoder.Save(o);
    }
  }
  public static string Download(string url,string dir,string stem,bool image) {
    string target=Path.Combine(dir,stem+(image?".jpg":".mp4"));
    if(File.Exists(target)) return "已存在，未覆盖："+Path.GetFileName(target);
    string temp=Path.Combine(dir,"下载中_"+Guid.NewGuid().ToString("N")+".part");
    string converted=temp+".jpg";
    try {
      using(var res=Open(url)) using(var input=res.GetResponseStream()) using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){input.CopyTo(file);}
      if(new FileInfo(temp).Length==0) throw new Exception("下载文件为空");
      if(image){
        ConvertToJpeg(temp,converted);
        File.Move(converted,target);
      } else File.Move(temp,target);
      return "成功："+Path.GetFileName(target);
    } finally {try{if(File.Exists(temp))File.Delete(temp);}catch{} try{if(File.Exists(converted))File.Delete(converted);}catch{}}
  }

}
