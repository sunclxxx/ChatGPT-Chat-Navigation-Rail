using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace LocalChatRail {
 static class PackageLaunch {
  public const string AppId="OpenAI.Codex_2p2nqsd0c76g0!App";
  [ComImport,Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Activation {
   [PreserveSig]int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)]string appId,[MarshalAs(UnmanagedType.LPWStr)]string arguments,uint options,out uint pid);
  }
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetPackageFullName(IntPtr process,ref uint length,StringBuilder name);
  public static bool HasIdentity(Process process){try{uint length=0;return GetPackageFullName(process.Handle,ref length,null)==122&&length>0;}catch{return false;}}
  public static Process Start(string arguments){object manager=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45ba127d-10a8-46ea-8ab7-56ea9078943c")));try{uint pid;Marshal.ThrowExceptionForHR(((Activation)manager).ActivateApplication(AppId,arguments,0,out pid));var process=Process.GetProcessById((int)pid);uint length=0;int result=GetPackageFullName(process.Handle,ref length,null);if(result!=122||length==0){process.Dispose();throw new IOException("Official package identity unavailable");}return process;}finally{Marshal.FinalReleaseComObject(manager);}}
 }
 static class Native {
  delegate bool EnumProc(IntPtr h, IntPtr a);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr arg);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
  [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
  [DllImport("iphlpapi.dll", SetLastError=true)] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int kind, uint reserved);
  [StructLayout(LayoutKind.Sequential)] struct KEY { public ushort vk, scan; public uint flags, time; public UIntPtr extra; }
  [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public KEY key; [FieldOffset(0)] public MOUSE mouse; }
  [StructLayout(LayoutKind.Sequential)] struct MOUSE { public int x,y; public uint data,flags,time; public UIntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION value; }
  static INPUT Key(ushort vk, bool up) { return new INPUT { type=1, value=new UNION { key=new KEY {vk=vk,flags=up ? 2u : 0u} } }; }
  static bool Belongs(IntPtr h,int pid) { uint p; GetWindowThreadProcessId(h,out p); return p==pid; }
  public static bool RequestQuit(int pid) {
   // Use the application's verified Ctrl+Q exit command, never terminate its process.
   for(int i=0;i<30;i++) {
    bool held=false; foreach(int key in new[]{0x10,0x11,0x12,0x5b,0x5c,0x51}) if(GetAsyncKeyState(key)<0) held=true;
    if(!held) break; if(i==29) return false; Thread.Sleep(100);
   }
   IntPtr window=GetForegroundWindow();
   if(!Belongs(window,pid)) {
    window=IntPtr.Zero;
    EnumWindows((h,a)=>{if(IsWindowVisible(h) && Belongs(h,pid)){window=h;return false;}return true;},IntPtr.Zero);
    if(window==IntPtr.Zero) return false;
    ShowWindow(window,9); SetForegroundWindow(window); Thread.Sleep(150);
   }
   if(!Belongs(GetForegroundWindow(),pid)) return false;
   var inputs=new[]{Key(0x11,false),Key(0x51,false),Key(0x51,true),Key(0x11,true)};
   uint sent=SendInput(4,inputs,Marshal.SizeOf(typeof(INPUT)));
   if(sent!=4) { SendInput(2,new[]{Key(0x51,true),Key(0x11,true)},Marshal.SizeOf(typeof(INPUT))); return false; }
   return true;
  }
  public static int PortOwner(int port) {
   int size=0; GetExtendedTcpTable(IntPtr.Zero,ref size,false,2,3,0);
   if(size<4 || size>4*1024*1024) return 0;
   IntPtr memory=Marshal.AllocHGlobal(size);
   try {
    if(GetExtendedTcpTable(memory,ref size,false,2,3,0)!=0) return 0;
    int rows=Marshal.ReadInt32(memory);
    for(int i=0;i<rows;i++) {
     int at=4+i*24; if(at+24>size) break;
     int state=Marshal.ReadInt32(memory,at),raw=Marshal.ReadInt32(memory,at+8);
     int local=((raw&255)<<8)|((raw>>8)&255);
     if(state==2 && local==port) return Marshal.ReadInt32(memory,at+20);
    }
   } finally {Marshal.FreeHGlobal(memory);} return 0;
  }
 }
 static class Watcher {
  const int Port=9335;
  static readonly string Folder=AppDomain.CurrentDomain.BaseDirectory;
  static void Log(string message) {
   try {var path=Path.Combine(Folder,"watcher.log");if(File.Exists(path)&&new FileInfo(path).Length>32768)File.Delete(path);File.AppendAllText(path,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+message+Environment.NewLine);}catch{}
  }
  static string OfficialPath(Process p) {
   try {string path=p.MainModule.FileName;
    if(!path.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"WindowsApps","OpenAI.Codex_"),StringComparison.OrdinalIgnoreCase) || !path.EndsWith("\\app\\ChatGPT.exe",StringComparison.OrdinalIgnoreCase))return null;
    return path;
   } catch {return null;}
  }
  static bool Prepare(string exe) {

   var psi=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32\\WindowsPowerShell\\v1.0\\powershell.exe"));
   psi.Arguments="-NoProfile -ExecutionPolicy Bypass -File \""+Path.Combine(Folder,"RefreshAdapter.ps1")+"\" -PackageRoot \""+Directory.GetParent(Path.GetDirectoryName(exe)).FullName+"\"";
   psi.UseShellExecute=false;psi.CreateNoWindow=true;psi.RedirectStandardError=true;psi.RedirectStandardOutput=true;
   using(var p=Process.Start(psi)){p.BeginOutputReadLine();p.BeginErrorReadLine();if(!p.WaitForExit(20000)){try{p.Kill();}catch{}Log("Adapter refresh timed out; application untouched");return false;}if(p.ExitCode!=0){Log("Adapter refresh failed; application untouched");return false;}}
   return true;
  }
  static void Worker(int pid,bool show) {Process.Start(new ProcessStartInfo(Path.Combine(Folder,"ChatRailHost.exe"),pid.ToString()){UseShellExecute=false,CreateNoWindow=true});Log("Navigation started for PID "+pid);}
  [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CommandLineToArgvW(string command, out int count);
  [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
  static string Quote(string text) {
   var result=new StringBuilder("\"");int slashes=0;
   foreach(char c in text){if(c=='\\'){slashes++;continue;}if(c=='\"')result.Append('\\',slashes*2+1).Append(c);else result.Append('\\',slashes).Append(c);slashes=0;}
   return result.Append('\\',slashes*2).Append('"').ToString();
  }
  static string OriginalArguments(int pid) {
   // Preserve mode, file and protocol arguments without logging their contents.
   using(var process=new ManagementObject("Win32_Process.Handle='"+pid+"'")) {
    string command=Convert.ToString(process["CommandLine"]);if(string.IsNullOrWhiteSpace(command))throw new InvalidOperationException("Original launch arguments unavailable");
    int count;IntPtr items=CommandLineToArgvW(command,out count);if(items==IntPtr.Zero)throw new InvalidOperationException("Launch argument parse failed");
    try{var args=new List<string>();for(int i=1;i<count;i++){string value=Marshal.PtrToStringUni(Marshal.ReadIntPtr(items,i*IntPtr.Size));if(value.StartsWith("--remote-debugging-port=",StringComparison.Ordinal)||value.StartsWith("--remote-debugging-address=",StringComparison.Ordinal))continue;args.Add(Quote(value));}return string.Join(" ",args.ToArray());}finally{LocalFree(items);}
   }
  }
  static bool RequestBridgeQuit(int pid) {
   if(Native.PortOwner(Port)!=pid)return false;
   try{var request=(System.Net.HttpWebRequest)System.Net.WebRequest.Create("http://127.0.0.1:"+Port+"/json/list");request.Proxy=null;request.Timeout=1500;object[] targets;using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))targets=(object[])new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(reader.ReadToEnd());
    foreach(Dictionary<string,object> target in targets){if(!target.ContainsKey("webSocketDebuggerUrl")||Convert.ToString(target["type"])!="page"||!Convert.ToString(target["url"]).StartsWith("app://",StringComparison.Ordinal))continue;var uri=new Uri(Convert.ToString(target["webSocketDebuggerUrl"]));if(uri.Scheme!="ws"||uri.Host!="127.0.0.1")continue;
     using(var socket=new System.Net.WebSockets.ClientWebSocket())using(var cancel=new CancellationTokenSource(4000)){socket.ConnectAsync(uri,cancel.Token).GetAwaiter().GetResult();string expression="window.electronBridge?.windowType === 'electron' && window.electronBridge.sendMessageFromView({type:'quit-app'})";var bytes=Encoding.UTF8.GetBytes(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{id=1,method="Runtime.evaluate",@params=new{expression=expression,awaitPromise=false}}));socket.SendAsync(new ArraySegment<byte>(bytes),System.Net.WebSockets.WebSocketMessageType.Text,true,cancel.Token).GetAwaiter().GetResult();Log("Normal exit requested through official app bridge");return true;}
    }
   }catch{Log("App exit bridge unavailable");}return false;
  }
  static void Handle(Process p,string exe,string originalArguments,bool show) {
   try {
    if(!Prepare(exe))return;
    int owner=Native.PortOwner(Port);
    if(owner==p.Id&&PackageLaunch.HasIdentity(p)){Worker(p.Id,show);return;}
    if(owner!=0&&owner!=p.Id){Log("Connection port is occupied; application untouched");return;}
    Log("Requesting normal exit for PID "+p.Id);
    if(!RequestBridgeQuit(p.Id)&&!Native.RequestQuit(p.Id)){Log("Normal exit command unavailable; no restart attempted");return;}
    if(!p.WaitForExit(12000)){Log("Application did not exit normally; no restart attempted");return;}
    // A second ordinary launch during shutdown must not be closed or restarted again.
    bool remains=true;
    for(int wait=0;wait<20;wait++) {
     remains=false;foreach(var other in Process.GetProcessesByName("ChatGPT"))using(other){if(OfficialPath(other)!=null)remains=true;}
     if(!remains)break;Thread.Sleep(250);
    }
    if(remains){Log("Another app process remains; restart deferred");return;}
    using(var launched=PackageLaunch.Start(originalArguments+" --remote-debugging-address=127.0.0.1 --remote-debugging-port="+Port)) {
     for(int i=0;i<60;i++){Thread.Sleep(500);if(launched.HasExited){Log("Relaunched application exited before connection");return;}if(Native.PortOwner(Port)==launched.Id){Worker(launched.Id,show);return;}}
     Log("Relaunched application has no connection; no further restart");
    }
   }catch(Exception e){Log("Startup stopped: "+e.GetType().Name);}
  }
  [STAThread] static void Main(string[] args) {
   try {
    File.WriteAllText(Path.Combine(Folder,"reconnect.signal"),"");
    bool owned;using(var mutex=new Mutex(true,"Local\\ChatRailManualLauncher",out owned)) {
     if(!owned)return;
     bool launched=false;int launchedPid=0;var deadline=DateTime.UtcNow.AddSeconds(45);
     while(DateTime.UtcNow<deadline) {
      var processes=Process.GetProcessesByName("ChatGPT");
      try {
       var alive=new HashSet<int>();foreach(var process in processes)alive.Add(process.Id);
       var children=ChildProcesses(alive);
       foreach(var process in processes) {
        try {
         if(children.Contains(process.Id))continue;
         string exe=OfficialPath(process);if(exe==null)continue;
         if(process.Id==launchedPid && Native.PortOwner(Port)!=process.Id)continue;
         string originalArguments=OriginalArguments(process.Id);
         if(originalArguments.Contains("\"--type="))continue;
         Handle(process,exe,originalArguments,true);return;
        }catch(Exception e){Log("Manual connection stopped: "+e.GetType().Name);return;}
       }
       if(!launched) {
        launched=true;
        using(var opened=PackageLaunch.Start("--remote-debugging-address=127.0.0.1 --remote-debugging-port="+Port)){launchedPid=opened.Id;}
       }
      }finally{foreach(var process in processes)process.Dispose();}
      Thread.Sleep(300);
     }
     Log("Manual launch timed out; no background listener remains");
    }
   }catch(Exception e){Log("Manual launch failed: "+e.GetType().Name);}
  }
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ENTRY {public uint size,usage,pid;public IntPtr heap;public uint module,threads,parent;public int priority;public uint flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string exe;}
  [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32First(IntPtr snapshot,ref ENTRY entry);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32Next(IntPtr snapshot,ref ENTRY entry);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  static HashSet<int> ChildProcesses(HashSet<int> alive) {var children=new HashSet<int>();IntPtr snap=CreateToolhelp32Snapshot(2,0);if(snap==new IntPtr(-1))return new HashSet<int>(alive);try{var entry=new ENTRY{size=(uint)Marshal.SizeOf(typeof(ENTRY))};if(!Process32First(snap,ref entry))return new HashSet<int>(alive);do{if(alive.Contains((int)entry.pid)&&alive.Contains((int)entry.parent))children.Add((int)entry.pid);}while(Process32Next(snap,ref entry));return children;}finally{CloseHandle(snap);}}
 }
}
