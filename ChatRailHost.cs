using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LocalChatRail {
  class Connection : IDisposable {
    readonly ClientWebSocket socket = new ClientWebSocket();
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
    int sequence;
    public async Task Connect(string url) {
      var uri = new Uri(url);
      if (uri.Scheme != "ws" || (uri.Host != "127.0.0.1" && uri.Host != "localhost"))
        throw new InvalidOperationException("Only loopback CDP endpoints are allowed");
      using (var cancel = new CancellationTokenSource(4000)) await socket.ConnectAsync(uri, cancel.Token);
    }
    public async Task<Dictionary<string,object>> Call(string method, object parameters) {
      int id = ++sequence;
      byte[] request = Encoding.UTF8.GetBytes(json.Serialize(new { id = id, method = method, @params = parameters }));
      using (var cancel = new CancellationTokenSource(5000)) {
        await socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, cancel.Token);
        while (true) {
          using (var stream = new MemoryStream()) {
            var buffer = new byte[16384]; WebSocketReceiveResult chunk;
            do {
              chunk = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token);
              if (chunk.MessageType == WebSocketMessageType.Close) throw new IOException("Renderer closed");
              stream.Write(buffer, 0, chunk.Count);
              if (stream.Length > 4 * 1024 * 1024) throw new IOException("Unexpected response size");
            } while (!chunk.EndOfMessage);
            var response = json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(stream.ToArray()));
            object responseId;
            if (response.TryGetValue("id", out responseId) && Convert.ToInt32(responseId) == id) return response;
          }
        }
      }
    }
    public void Dispose() { socket.Abort(); socket.Dispose(); }
  }
  class Host : ApplicationContext {
    const int Port = 9335;
    readonly NotifyIcon tray = new NotifyIcon();
    readonly Icon trayIcon;
    readonly ContextMenuStrip menu;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly Dictionary<string,Connection> connections = new Dictionary<string,Connection>();
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
    readonly string folder = AppDomain.CurrentDomain.BaseDirectory;
    readonly string installExpression;
    bool running, exiting, reconnectRequested; readonly System.Diagnostics.Process parent;
    ToolStripMenuItem reconnectItem; bool connected;
    string lastState = "等待 ChatGPT 连接";
    public Host(System.Diagnostics.Process owner,bool show) { parent = owner;
      installExpression = "(() => { if (window.electronBridge?.windowType !== 'electron') return {installed:false}; " +
        "window.__CHAT_RAIL_ADAPTER__ = " + File.ReadAllText(Path.Combine(folder,"adapter.json")) + "; return " +
        File.ReadAllText(Path.Combine(folder,"navigation.js")).Trim().TrimEnd(';') + "; })()";
      menu = RailUi.Menu();
      reconnectItem = new ToolStripMenuItem("重新连接（未连接）",null,(s,e)=>{reconnectRequested=true;}); reconnectItem.Tag="refresh";menu.Items.Add(reconnectItem);
      menu.Opening+=(s,e)=>{reconnectItem.Text="重新连接（"+(connected?"已连接":"未连接")+"）";};
      menu.Items.Add("使用说明", null, (s,e) => new RailDialog("使用说明","悬停横条：预览消息和发送时间\n点击横条：定位消息\n点击书签：收藏或取消收藏\n\n完全退出 ChatGPT 后，导航一起退出。").Show());
      menu.Items[1].Tag="book";
      menu.Items.Add(new ToolStripSeparator());
      menu.Items.Add("关闭本次导航", null, async (s,e) => await Stop());
      trayIcon = new Icon(Path.Combine(folder,"NavigationRail.ico")); tray.Icon = trayIcon; tray.Text = "Chat Navigation Rail";
      tray.Visible = true; tray.ContextMenuStrip = menu;
      tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left){menu.Show(Cursor.Position);}};
      
      timer.Interval = 2000; timer.Tick += async (s,e) => await Poll(); timer.Start();
    }
    object[] Targets() {
      var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + "/json/list");
      request.Proxy = null; request.Timeout = 1200;
      using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream()))
        return (object[])json.DeserializeObject(reader.ReadToEnd());
    }
    async Task Poll() {
      if(exiting)return;
      string signal=Path.Combine(folder,"reconnect.signal");try{if(File.Exists(signal)){File.Delete(signal);reconnectRequested=true;}}catch(IOException){}
      if(running)return;
      if (parent.HasExited || File.Exists(Path.Combine(folder,"uninstall.signal"))) { await Stop(); return; }
      if (Native.PortOwner(Port) != parent.Id) { connected=false; lastState = "等待 ChatGPT 本机连接"; return; }
      running = true; connected=false;
      try {
        bool refreshHistory=reconnectRequested;
        if(reconnectRequested){foreach(var c in connections.Values)c.Dispose();connections.Clear();reconnectRequested=false;}
        var targets = await Task.Run(() => Targets());
        var alive = new HashSet<string>(); int attached = 0;
        foreach (Dictionary<string,object> t in targets) {
          if (exiting) break;
          if (!t.ContainsKey("webSocketDebuggerUrl") || !t.ContainsKey("id") || Convert.ToString(t["type"]) != "page") continue;
          var url = Convert.ToString(t["url"]);
          // Exclude ordinary websites, browser tabs and MCP widget iframes.
          if (!(url.StartsWith("app://") || url.StartsWith("codex://") || url.StartsWith("file://"))) continue;
          string id = Convert.ToString(t["id"]); alive.Add(id); Connection client;
          try {
            if (!connections.TryGetValue(id, out client)) {
              client = new Connection(); connections[id] = client; await client.Connect(Convert.ToString(t["webSocketDebuggerUrl"]));
            }
            string expression = "window.__chatRail?.build === '20261003-manual' ? {installed:true,status:window.__chatRail.status} : {installed:false}";
            if(refreshHistory) await client.Call("Runtime.evaluate",new {expression="window.__chatRail?.refresh()",returnByValue=true});
            var result = await client.Call("Runtime.evaluate", new { expression = expression, returnByValue = true, awaitPromise = false });
            if (json.Serialize(result).Contains("\"installed\":false"))
              result = await client.Call("Runtime.evaluate", new { expression = installExpression, returnByValue = true, awaitPromise = false });
            var inner = result.ContainsKey("result") ? result["result"] as Dictionary<string,object> : null;
            var remote = inner != null && inner.ContainsKey("result") ? inner["result"] as Dictionary<string,object> : null;
            var value = remote != null && remote.ContainsKey("value") ? remote["value"] as Dictionary<string,object> : null;
            if (value != null && value.ContainsKey("installed") && Convert.ToBoolean(value["installed"])) {
              attached++; connected=true;
              var status = value.ContainsKey("status") ? value["status"] as Dictionary<string,object> : null;
              if(status!=null && status.ContainsKey("theme"))RailUi.Dark=Convert.ToString(status["theme"])!="light";
              if (status != null && Convert.ToBoolean(status["connected"]))
                lastState = "已连接 · " + status["count"] + " 条消息 · " + (Convert.ToBoolean(status["complete"]) ? "完整历史" : "历史读取中") +
                  (string.IsNullOrEmpty(Convert.ToString(status["error"])) ? "" : "\n" + status["error"]);
              else lastState = "已连接，请打开 Chat 对话";
            }
          } catch { if (connections.TryGetValue(id,out client)) client.Dispose(); connections.Remove(id); }
        }
        foreach (var id in new List<string>(connections.Keys)) if (!alive.Contains(id)) { connections[id].Dispose(); connections.Remove(id); }
        if (attached == 0) lastState = "调试连接存在，但未找到兼容的 ChatGPT 主窗口";
      } catch { lastState = "ChatGPT 未开启本机连接，导航尚未启用"; }
      finally { running = false; tray.Text = "Chat 导航：" + (lastState.Length > 48 ? lastState.Substring(0,48) : lastState);reconnectItem.Text="重新连接（"+(connected?"已连接":"未连接")+"）"; }
    }
    async Task Stop() {
      if (exiting) return; exiting = true; timer.Stop();
      // Wait for a current read instead of concurrently using the CDP socket.
      while (running) await Task.Delay(100);
      foreach (var client in connections.Values) {
        try { await client.Call("Runtime.evaluate", new { expression = "window.__chatRail?.stop()", returnByValue = true }); } catch {}
        client.Dispose();
      }
      connections.Clear(); tray.Visible = false; tray.Dispose(); trayIcon.Dispose(); menu.Dispose(); timer.Dispose(); parent.Dispose(); ExitThread();
    }
  }
  static class Program {
    [STAThread] static void Main(string[] args) {
      bool acquired;
      using (var mutex = new Mutex(true,"Local\\ChatRailStandaloneHost",out acquired)) {
        if (!acquired){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reconnect.signal"),"");return;}
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                int pid;
        if(args.Length<1 || !int.TryParse(args[0],out pid)) return;
        System.Diagnostics.Process owner;
        try {owner=System.Diagnostics.Process.GetProcessById(pid); if(owner.ProcessName!="ChatGPT" || owner.HasExited)return;} catch{return;}
        Application.Run(new Host(owner,Array.IndexOf(args,"--show")>=0));
      }
    }
  }
}
