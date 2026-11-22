using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

internal sealed class LivePairing
{
    internal string Host; internal int Port; internal byte[] Key;
    internal static LivePairing Parse(string text) {
        if(text==null || text.Length>4096)throw new FormatException("Pairing file is too large");
        string[] lines=text.Replace("\r","").Trim().Split('\n');
        if(lines.Length!=4 || lines[0]!="TIZENPSP_LIVE_V1" || !lines[1].StartsWith("host=",StringComparison.Ordinal) ||
           !lines[2].StartsWith("port=",StringComparison.Ordinal) || !lines[3].StartsWith("key=",StringComparison.Ordinal))throw new FormatException("Invalid pairing file");
        IPAddress ip;int port;
        if(!IPAddress.TryParse(lines[1].Substring(5),out ip) || ip.AddressFamily!=AddressFamily.InterNetwork)throw new FormatException("Use a local IPv4 address");
        byte[] b=ip.GetAddressBytes();
        if(!(b[0]==10 || b[0]==127 || (b[0]==192&&b[1]==168) || (b[0]==172&&b[1]>=16&&b[1]<=31) || (b[0]==169&&b[1]==254)))throw new FormatException("Use a private LAN address");
        if(!int.TryParse(lines[2].Substring(5),out port) || port<1024 || port>65535)throw new FormatException("Invalid port");
        string key=lines[3].Substring(4);if(key.Length!=64)throw new FormatException("Invalid pairing key");
        byte[] secret=new byte[32];for(int i=0;i<32;i++)secret[i]=byte.Parse(key.Substring(i*2,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        return new LivePairing{Host=ip.ToString(),Port=port,Key=secret};
    }
}

// Authenticated, sequenced messages. The key is never sent over the network.
// Local-network telemetry is authenticated, not encrypted. No shell/file commands.
internal static class LiveWire
{
    internal const int MaxPayload=32768,MaxFrame=44000;
    internal static string Hex(byte[] b)=>BitConverter.ToString(b).Replace("-","").ToLowerInvariant();
    internal static string Nonce(){var b=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(b);return Hex(b);}
    internal static byte[] Mac(byte[] key,string text){using(var h=new HMACSHA256(key))return h.ComputeHash(Encoding.UTF8.GetBytes(text));}
    internal static bool Equal(string a,string b){if(a==null||b==null||a.Length!=b.Length)return false;int d=0;for(int i=0;i<a.Length;i++)d|=a[i]^b[i];return d==0;}
    internal static bool ValidNonce(string value){if(value==null||value.Length!=64)return false;foreach(char c in value)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')))return false;return true;}
    internal static void Write(Stream stream,string line){byte[] b=Encoding.ASCII.GetBytes(line+"\n");stream.Write(b,0,b.Length);}
    internal static byte[] Authenticate(Stream stream,byte[] key,LiveReader reader) {
        string first=reader.Read(128);if(!first.StartsWith("CHALLENGE ",StringComparison.Ordinal))throw new IOException("Receiver handshake missing");
        string pc=first.Substring(10);if(!ValidNonce(pc))throw new IOException("Invalid receiver challenge");
        string tv=Nonce(),context=pc+"|"+tv;
        Write(stream,"HELLO "+tv+" "+Hex(Mac(key,"TV|"+context)));
        if(!Equal(reader.Read(128),"WELCOME "+Hex(Mac(key,"PC|"+context))))throw new IOException("Receiver authentication failed");
        return Mac(key,"SESSION|"+context);
    }
    internal static string Pack(byte[] key,string direction,ulong sequence,string payload) {
        byte[] bytes=Encoding.UTF8.GetBytes(payload);if(bytes.Length>MaxPayload)throw new IOException("Live payload too large");
        string body=sequence.ToString(CultureInfo.InvariantCulture)+" "+Convert.ToBase64String(bytes);
        return body+" "+Hex(Mac(key,direction+"|"+body));
    }
    internal static string Unpack(byte[] key,string direction,ulong sequence,string frame) {
        string[] p=frame.Split(' ');ulong actual;
        if(p.Length!=3 || !ulong.TryParse(p[0],out actual) || actual!=sequence ||
           !Equal(p[2],Hex(Mac(key,direction+"|"+p[0]+" "+p[1]))))throw new IOException("Invalid live signature/sequence");
        byte[] bytes=Convert.FromBase64String(p[1]);if(bytes.Length>MaxPayload)throw new IOException("Live payload too large");
        return new UTF8Encoding(false,true).GetString(bytes);
    }
}

internal sealed class LiveReader
{
    readonly Stream stream;readonly byte[] buffer=new byte[4096];int offset,count;
    internal LiveReader(Stream stream){this.stream=stream;}
    internal bool Pending=>offset<count;
    internal string Read(int limit=LiveWire.MaxFrame) {
        var line=new StringBuilder();
        while(true) {
            if(offset==count){count=stream.Read(buffer,0,buffer.Length);offset=0;if(count==0)throw new EndOfStreamException();}
            byte c=buffer[offset++];if(c==10)return line.ToString();
            if(c<32||c>126||line.Length>=limit)throw new IOException("Invalid live frame");line.Append((char)c);
        }
    }
}

internal sealed class LiveConnection : IDisposable
{
    readonly object gate=new object();readonly LivePairing pairing;
    readonly Queue<string> logs=new Queue<string>(),control=new Queue<string>();
    readonly Queue<KeyValuePair<int,string>> commands=new Queue<KeyValuePair<int,string>>();
    readonly ManualResetEvent stop=new ManualResetEvent(false);
    Thread worker;TcpClient socket;int queuedChars,generation;long dropped;bool connected,disposed;
    string state="Off";
    internal LiveConnection(LivePairing pairing){this.pairing=pairing;}
    internal bool Connected {get{lock(gate)return connected;}}
    internal int Generation {get{lock(gate)return generation;}}
    internal long Dropped {get{lock(gate)return dropped;}}
    internal string Status {get{lock(gate)return state+" | dropped="+dropped;}}
    internal void Start(){lock(gate){if(worker!=null||disposed)return;worker=new Thread(Run){IsBackground=true,Name="Live diagnostics"};worker.Start();}}
    internal void OfferLog(string line) {
        lock(gate){if(!connected||disposed)return;if(line.Length>20000||(line.Length>8191&&Encoding.UTF8.GetByteCount(line)+4>LiveWire.MaxPayload)||logs.Count>=256||queuedChars+line.Length>262144){dropped++;return;}logs.Enqueue(line);queuedChars+=line.Length;}
    }
    internal bool Send(string payload){lock(gate){if(!connected||disposed||control.Count>=32||payload.Length>30000||(payload.Length>8192&&Encoding.UTF8.GetByteCount(payload)>LiveWire.MaxPayload)){dropped++;return false;}control.Enqueue(payload);return true;}}
    internal bool TryCommand(out string command){int epoch;return TryCommand(out command,out epoch);}
    internal bool TryCommand(out string command,out int epoch){lock(gate){if(connected&&commands.Count>0){var item=commands.Dequeue();command=item.Value;epoch=item.Key;return true;}command=null;epoch=0;return false;}}
    string Take(){lock(gate){if(control.Count>0)return control.Dequeue();if(logs.Count==0)return null;string line=logs.Dequeue();queuedChars-=line.Length;return "LOG\n"+line;}}
    void Disconnect(string reason){lock(gate){connected=false;state=reason;logs.Clear();control.Clear();commands.Clear();queuedChars=0;}}
    void Run() {
        while(!stop.WaitOne(0)) {
            TcpClient client=null;
            string failure="Disconnected; retrying";
            try {
                lock(gate){if(disposed)break;state="Connecting to "+pairing.Host;socket=client=new TcpClient(AddressFamily.InterNetwork);}
                var pending=client.ConnectAsync(IPAddress.Parse(pairing.Host),pairing.Port);
                if(!pending.Wait(3000))throw new IOException("Connect timeout");pending.GetAwaiter().GetResult();
                client.NoDelay=true;var stream=client.GetStream();stream.ReadTimeout=3000;stream.WriteTimeout=1500;
                var reader=new LiveReader(stream);byte[] key=LiveWire.Authenticate(stream,pairing.Key,reader);ulong tx=0,rx=0;
                lock(gate){if(disposed)break;connected=true;generation++;state="Connected to "+pairing.Host;}
                var received=Stopwatch.StartNew();
                while(!stop.WaitOne(0)) {
                    for(int i=0;i<16;i++){string data=Take();if(data==null)break;LiveWire.Write(stream,LiveWire.Pack(key,"T",++tx,data));}
                    if(reader.Pending || client.Client.Poll(50000,SelectMode.SelectRead)) {
                        string command=LiveWire.Unpack(key,"P",++rx,reader.Read());received.Restart();
                        if(command=="PING")LiveWire.Write(stream,LiveWire.Pack(key,"T",++tx,"PONG"));
                        else {lock(gate){if(command.Length<=128&&commands.Count<8)commands.Enqueue(new KeyValuePair<int,string>(generation,command));else throw new IOException("Command limit");}}
                    }
                    if(received.ElapsedMilliseconds>12000)throw new IOException("Receiver heartbeat timeout");
                }
            } catch(Exception e) {
                Exception cause=e.GetBaseException();
                failure="Retrying: "+(cause is SocketException s?s.SocketErrorCode.ToString():cause.GetType().Name);
            }
            finally {Disconnect(failure);try{client?.Close();}catch{}lock(gate)socket=null;}
            if(stop.WaitOne(3000))break;
        }
        Disconnect("Off");
        stop.Dispose();
    }
    public void Dispose(){lock(gate){if(disposed)return;disposed=true;connected=false;state="Off";stop.Set();try{socket?.Close();}catch{}if(worker==null)stop.Dispose();}}
    internal bool WaitStopped(int milliseconds)=>worker==null||worker.Join(milliseconds);
}
