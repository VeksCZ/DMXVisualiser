using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// DMX přijímač: Art-Net (UDP 6454) i sACN / E1.31 (UDP 5568, multicast i unicast).
// Data ukládá do UniverseStore (až 32768 universů, kontrola pořadí paketů a priorit)
// a na ArtPoll odpovídá, aby se aplikace v SoundSwitchi ukázala jako Art-Net zařízení.
// Parsování paketů je v DmxProtocol.cs (bez Unity, má unit testy).
//
// Na stejném PC drží port 6454 i SoundSwitch (0.0.0.0:6454). Proto se navazujeme
// přímo na IP adresy počítače – konkrétní adresa má ve Windows přednost před 0.0.0.0,
// takže unicast data pro visualizér dostaneme my, ne SoundSwitch.
public class ArtNetReceiver : MonoBehaviour
{
    public string nodeName = "DJ Visualizer";
    public int port = 6454;

    [Header("Stav (jen pro čtení)")]
    public int packetsReceived;     // Art-Net + sACN dohromady
    public int sacnPackets;
    public int pollsAnswered;
    public int droppedPackets;      // pozdě došlé (mimo pořadí)
    public float lastPacketTime = -999f;
    public string lastSender;
    public string lastProtocol;     // "Art-Net" / "sACN"
    public string bindInfo;

    [Tooltip("Poslouchat i sACN (E1.31) na UDP 5568")]
    public bool sacnEnabled = true;

    readonly UniverseStore store = new UniverseStore();
    readonly List<Listener> listeners = new List<Listener>();
    volatile bool running;
    int packetCounter, sacnCounter, pollCounter;
    volatile bool gotPacket;
    string senderStr, protoStr;
    volatile int[] advertised = new int[0];     // universa (od 0) z patche – nabídnou se v ArtPollReply

    // sACN: jeden socket na 0.0.0.0:5568, členství v multicastových skupinách podle universů v patchi
    UdpClient sacn;
    Thread sacnThread;
    readonly List<IPAddress> joinIfaces = new List<IPAddress>();
    readonly HashSet<int> joined = new HashSet<int>();      // čísla universů podle E1.31 (od 1)
    const int MaxMulticastJoins = 20;                       // limit Windows i Linuxu na jeden socket

    static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    static long NowMs => clock.ElapsedMilliseconds;

    class Listener
    {
        public UdpClient udp;
        public IPAddress ip;
        public Thread thread;
    }

    public bool HasData => Time.time - lastPacketTime < 2f;

    void OnEnable()
    {
        running = true;

        var bound = new List<string>();
#if UNITY_ANDROID && !UNITY_EDITOR
        // Quest (Linux): socket navázaný na konkrétní IP nedostává broadcast (ArtPoll),
        // proto 0.0.0.0 + multicast lock, jinak Wi-Fi broadcasty zahazuje.
        AcquireMulticastLock();
        if (TryListen(IPAddress.Any)) bound.Add("0.0.0.0");
        joinIfaces.Add(IPAddress.Any);                  // multicast přes výchozí rozhraní
#else
        foreach (var ip in LocalIPv4())
            if (TryListen(ip)) bound.Add(ip.ToString());
        if (bound.Count == 0 && TryListen(IPAddress.Any)) bound.Add("0.0.0.0");
        joinIfaces.AddRange(LocalIPv4());
#endif

        bindInfo = bound.Count > 0 ? string.Join(", ", bound) + " : " + port : "port " + port + " nejde otevřít";
        if (sacnEnabled && StartSacn()) bindInfo += "  |  sACN : " + SacnProtocol.Port;
        Debug.Log("Art-Net: poslouchám na " + bindInfo);
    }

    bool TryListen(IPAddress ip)
    {
        try
        {
            var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(ip, port));
            udp.EnableBroadcast = true;
            var l = new Listener { udp = udp, ip = ip };
            l.thread = new Thread(() => Loop(l)) { IsBackground = true, Name = "ArtNet " + ip };
            listeners.Add(l);
            l.thread.Start();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Art-Net: " + ip + ":" + port + " – " + e.Message);
            return false;
        }
    }

    void OnDisable()
    {
        running = false;
        foreach (var l in listeners) { try { l.udp.Close(); } catch { } }
        listeners.Clear();
        try { sacn?.Close(); } catch { }
        sacn = null;
        joined.Clear();
        joinIfaces.Clear();
#if UNITY_ANDROID && !UNITY_EDITOR
        try { multicastLock?.Call("release"); } catch { }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject multicastLock;
    float nextAnnounce;

    void AcquireMulticastLock()
    {
        try
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = up.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi"))
            {
                multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "artnet");
                multicastLock.Call("setReferenceCounted", false);
                multicastLock.Call("acquire");
            }
        }
        catch (Exception e) { Debug.LogWarning("Art-Net: multicast lock – " + e.Message); }
    }

    // Kdyby se ArtPoll přes Wi-Fi ztratil, ohlásíme se sami každé 3 s.
    void Announce()
    {
        if (Time.unscaledTime < nextAnnounce || listeners.Count == 0) return;
        nextAnnounce = Time.unscaledTime + 3f;
        try
        {
            foreach (var reply in ArtNetProtocol.BuildPollReplies(DefaultIP().GetAddressBytes(), nodeName, advertised))
                listeners[0].udp.Send(reply, reply.Length, new IPEndPoint(IPAddress.Broadcast, port));
        }
        catch { }
    }
#endif

    void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        Announce();
#endif
        if (gotPacket)
        {
            gotPacket = false;
            lastPacketTime = Time.time;
            lastSender = senderStr;
            lastProtocol = protoStr;
        }
        packetsReceived = packetCounter;
        sacnPackets = sacnCounter;
        pollsAnswered = pollCounter;
        droppedPackets = (int)store.Dropped;
    }

    // Kopie aktuálních dat universe (od 0, tj. SoundSwitch „Universe 1“ = 0).
    public bool GetUniverse(int universe, byte[] dest) => store.Get(universe, dest);

    // Kolik universů právě dostává data.
    public int ActiveUniverses => store.ActiveCount(NowMs);
    public bool UniverseActive(int universe) => store.IsActive(universe, NowMs);

    // Universa použitá v patchi (od 0): ohlásí se v ArtPollReply a přihlásí se k nim sACN multicast.
    // Volat z hlavního vlákna, klidně často – nic se nedělá, když se seznam nezměnil.
    public void SetUniverses(IEnumerable<int> universes)
    {
        var list = new SortedSet<int>();
        if (universes != null) foreach (var u in universes) if (u >= 0 && u < UniverseStore.MaxUniverses) list.Add(u);
        var arr = new int[list.Count];
        list.CopyTo(arr);
        bool same = arr.Length == advertised.Length;
        for (int i = 0; same && i < arr.Length; i++) same = arr[i] == advertised[i];
        if (same) return;
        advertised = arr;
        UpdateMulticast(list);
    }

    void Loop(Listener l)
    {
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            byte[] p;
            try { p = l.udp.Receive(ref ep); }
            catch { if (!running) break; Thread.Sleep(1); continue; }

            int op = ArtNetProtocol.OpCode(p, p.Length);
            if (op == ArtNetProtocol.OpDmx)
            {
                if (ArtNetProtocol.TryParseDmx(p, p.Length, out var d)) Accept(d, p, ep.Address, "Art-Net");
            }
            else if (op == ArtNetProtocol.OpPoll)
            {
                try
                {
                    var myIp = l.ip.Equals(IPAddress.Any) ? DefaultIP() : l.ip;
                    foreach (var reply in ArtNetProtocol.BuildPollReplies(myIp.GetAddressBytes(), nodeName, advertised))
                    {
                        l.udp.Send(reply, reply.Length, new IPEndPoint(ep.Address, port));
                        l.udp.Send(reply, reply.Length, new IPEndPoint(IPAddress.Broadcast, port));
                    }
                    Interlocked.Increment(ref pollCounter);
                }
                catch { }
            }
        }
    }

    void Accept(DmxPacket d, byte[] p, IPAddress from, string proto)
    {
        if (!store.Submit(d.universe, p, d.offset, d.length, d.sequence, d.priority, NowMs)) return;
        Interlocked.Increment(ref packetCounter);
        if (proto == "sACN") Interlocked.Increment(ref sacnCounter);
        senderStr = from.ToString();
        protoStr = proto;
        gotPacket = true;
    }

    // ---- sACN (E1.31) ----

    bool StartSacn()
    {
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, SacnProtocol.Port));
            sacn = udp;
            sacnThread = new Thread(SacnLoop) { IsBackground = true, Name = "sACN" };
            sacnThread.Start();
            UpdateMulticast(new SortedSet<int>(advertised));
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("sACN: port " + SacnProtocol.Port + " – " + e.Message);
            sacn = null;
            return false;
        }
    }

    void SacnLoop()
    {
        var udp = sacn;
        var ep = new IPEndPoint(IPAddress.Any, 0);
        while (running && udp != null)
        {
            byte[] p;
            try { p = udp.Receive(ref ep); }
            catch { if (!running) break; Thread.Sleep(1); continue; }
            if (!SacnProtocol.TryParse(p, p.Length, out var d, out _)) continue;
            if (d.terminated) { store.Release(d.universe); continue; }
            Accept(d, p, ep.Address, "sACN");
        }
    }

    // Přihlásí multicastové skupiny pro universa v patchi (a odhlásí ta, která z patche zmizela).
    void UpdateMulticast(SortedSet<int> universesZeroBased)
    {
        var s = sacn;
        if (s == null || !running) return;
        var want = new HashSet<int>();
        foreach (var u in universesZeroBased) want.Add(u + 1);

        foreach (var n in new List<int>(joined))
            if (!want.Contains(n)) { Membership(s, n, false); joined.Remove(n); }

        foreach (var n in want)
        {
            if (joined.Contains(n)) continue;
            if ((joined.Count + 1) * Math.Max(1, joinIfaces.Count) > MaxMulticastJoins)
            {
                Debug.LogWarning("sACN: moc universů pro multicast (max " + MaxMulticastJoins + " členství), universe " + n + " se nepřihlásí – unicast funguje dál");
                continue;
            }
            Membership(s, n, true);
            joined.Add(n);
        }
    }

    void Membership(UdpClient s, int universeNumber, bool join)
    {
        var group = SacnProtocol.MulticastAddress(universeNumber);
        foreach (var ifc in joinIfaces)
        {
            try
            {
                s.Client.SetSocketOption(SocketOptionLevel.IP, join ? SocketOptionName.AddMembership : SocketOptionName.DropMembership,
                    new MulticastOption(group, ifc));
            }
            catch (Exception e) { if (join) Debug.LogWarning("sACN: " + group + " na " + ifc + " – " + e.Message); }
        }
    }

    static List<IPAddress> LocalIPv4()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !list.Contains(ua.Address))
                        list.Add(ua.Address);
            }
        }
        catch (Exception e) { Debug.LogWarning("Art-Net: seznam IP nejde zjistit – " + e.Message); }
        return list;
    }

    static IPAddress DefaultIP()
    {
        try
        {
            using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                s.Connect("8.8.8.8", 65530);
                return ((IPEndPoint)s.LocalEndPoint).Address;
            }
        }
        catch
        {
            var l = LocalIPv4(); // síť bez výchozí brány (hotspot bez internetu)
            return l.Count > 0 ? l[0] : IPAddress.Loopback;
        }
    }
}
