using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

// ============================================================================
// DMX po síti bez závislosti na Unity (jde otestovat v běžném .NET – viz Tools/Tests):
//  • ArtNetProtocol – ArtDmx / ArtPoll / ArtPollReply
//  • SacnProtocol   – sACN (E1.31) datagram a multicastová adresa universa
//  • UniverseStore  – vlákno-bezpečné úložiště universů (15bitová adresa, priorita, pořadí paketů)
// Universa se zde číslují od 0 (Art-Net „port-address“). sACN universe N → index N-1,
// stejně jako v SoundSwitchi a v UI aplikace (universe 1 = Art-Net universe 0).
// ============================================================================

// Jeden přijatý DMX rámec (data jsou v původním bufferu od offset, length bajtů).
public struct DmxPacket
{
    public int universe;    // index universa od 0
    public int sequence;    // 0 = pořadí se nekontroluje (Art-Net); sACN 0–255
    public int priority;    // sACN 0–200, Art-Net vždy 100
    public int offset;
    public int length;
    public bool terminated; // sACN: zdroj hlásí konec streamu
}

public static class ArtNetProtocol
{
    public const int Port = 6454;
    public const int OpPoll = 0x2000, OpPollReply = 0x2100, OpDmx = 0x5000;
    public const int DefaultPriority = 100;
    static readonly byte[] Header = Encoding.ASCII.GetBytes("Art-Net\0");

    // Operační kód paketu, nebo -1 když to není Art-Net.
    public static int OpCode(byte[] p, int n)
    {
        if (p == null || n < 10 || p.Length < n) return -1;
        for (int i = 0; i < 8; i++) if (p[i] != Header[i]) return -1;
        return p[8] | (p[9] << 8);
    }

    // ArtDmx: Sequence p[12], SubUni p[14], Net p[15] (7 bitů), Length p[16..17] (big endian).
    public static bool TryParseDmx(byte[] p, int n, out DmxPacket r)
    {
        r = default(DmxPacket);
        if (OpCode(p, n) != OpDmx || n < 18) return false;
        int len = (p[16] << 8) | p[17];
        len = Math.Min(Math.Min(len, 512), n - 18);
        if (len <= 0) return false;
        r.universe = p[14] | ((p[15] & 0x7F) << 8);
        r.sequence = p[12];
        r.priority = DefaultPriority;
        r.offset = 18;
        r.length = len;
        return true;
    }

    // ArtPollReply. Každá odpověď popisuje nejvýše 4 porty ve stejném Net/SubNet (universa se stejným port-address >> 4),
    // další skupiny dostanou vyšší BindIndex. Bez universů se ohlásí 0–3 (SoundSwitch tak nabídne Universe 1–4).
    public static List<byte[]> BuildPollReplies(byte[] ip, string nodeName, IEnumerable<int> universes)
    {
        var set = new SortedSet<int>();
        for (int i = 0; i < 4; i++) set.Add(i);
        if (universes != null)
            foreach (var u in universes)
                if (u >= 0 && u < UniverseStore.MaxUniverses) set.Add(u);

        var replies = new List<byte[]>();
        var group = new List<int>();
        int bind = 1;
        foreach (var u in set)
        {
            if (group.Count > 0 && (group.Count == 4 || (group[0] >> 4) != (u >> 4)))
            {
                replies.Add(BuildPollReply(ip, nodeName, group, bind++));
                group.Clear();
            }
            group.Add(u);
        }
        if (group.Count > 0) replies.Add(BuildPollReply(ip, nodeName, group, bind));
        return replies;
    }

    static byte[] BuildPollReply(byte[] ip, string nodeName, List<int> ports, int bindIndex)
    {
        var r = new byte[239];
        Buffer.BlockCopy(Header, 0, r, 0, 8);
        r[8] = 0x00; r[9] = 0x21;                       // OpPollReply
        Buffer.BlockCopy(ip, 0, r, 10, 4);
        r[14] = 0x36; r[15] = 0x19;                     // port 6454 (LE)
        r[16] = 0; r[17] = 1;                           // verze firmwaru
        r[18] = (byte)((ports[0] >> 8) & 0x7F);         // NetSwitch
        r[19] = (byte)((ports[0] >> 4) & 0x0F);         // SubSwitch
        r[20] = 0xFF; r[21] = 0xFF;                     // OEM
        r[23] = 0xD0;                                   // Status1
        WriteAscii(r, 26, nodeName, 17);
        WriteAscii(r, 44, nodeName + " (Unity)", 63);
        WriteAscii(r, 108, "#0001 [0000] OK", 63);
        r[172] = 0; r[173] = (byte)ports.Count;         // počet portů
        for (int i = 0; i < ports.Count; i++)
        {
            r[174 + i] = 0x80;                          // výstup DMX = přijímá Art-Net
            r[182 + i] = 0x80;
            r[190 + i] = (byte)(ports[i] & 0x0F);       // universe v rámci SubNetu
        }
        r[200] = 0x00;                                  // StNode
        Buffer.BlockCopy(ip, 0, r, 207, 4);
        r[211] = (byte)bindIndex;
        r[212] = 0x08;                                  // Status2: 15bitový port-address
        return r;
    }

    static void WriteAscii(byte[] dst, int offset, string s, int max)
    {
        var b = Encoding.ASCII.GetBytes(s ?? "");
        Buffer.BlockCopy(b, 0, dst, offset, Math.Min(b.Length, max));
    }
}

public static class SacnProtocol
{
    public const int Port = 5568;
    public const int HeaderLength = 126;            // data začínají na 126 (index 125 je start code)
    public const int DefaultPriority = 100;
    static readonly byte[] AcnId = { 0x41, 0x53, 0x43, 0x2D, 0x45, 0x31, 0x2E, 0x31, 0x37, 0x00, 0x00, 0x00 };   // "ASC-E1.17\0\0\0"

    // Multicastová adresa universa: 239.255.(u>>8).(u&255); u je číslo universa podle E1.31 (od 1).
    public static IPAddress MulticastAddress(int universeNumber) =>
        new IPAddress(new byte[] { 239, 255, (byte)(universeNumber >> 8), (byte)(universeNumber & 0xFF) });

    // Datagram E1.31 s DMX daty (start code 0). Vrací false pro cokoli jiného (sync, discovery, NULL start code…).
    public static bool TryParse(byte[] p, int n, out DmxPacket r, out string sourceName)
    {
        r = default(DmxPacket);
        sourceName = null;
        if (p == null || n <= HeaderLength || p.Length < n) return false;
        if (p[0] != 0x00 || p[1] != 0x10 || p[2] != 0x00 || p[3] != 0x00) return false;     // preamble 0x0010, postamble 0
        for (int i = 0; i < 12; i++) if (p[4 + i] != AcnId[i]) return false;
        if (U32(p, 18) != 0x00000004) return false;     // root vector: E1.31 data
        if (U32(p, 40) != 0x00000002) return false;     // framing vector: data packet
        if (p[117] != 0x02 || p[118] != 0xA1) return false;   // DMP: set property, adresy+data
        if (p[125] != 0x00) return false;               // jen start code 0 (DMX)
        int count = (p[123] << 8) | p[124];             // počet hodnot včetně start code
        int len = Math.Min(Math.Min(count - 1, 512), n - HeaderLength);
        if (len <= 0) return false;
        int number = (p[113] << 8) | p[114];
        if (number < 1 || number > 63999) return false;
        r.universe = number - 1;
        r.sequence = p[111];
        r.priority = Math.Min((int)p[108], 200);
        r.offset = HeaderLength;
        r.length = len;
        r.terminated = (p[112] & 0x40) != 0;
        int end = 44;
        while (end < 108 && p[end] != 0) end++;
        sourceName = Encoding.UTF8.GetString(p, 44, end - 44);
        return true;
    }

    static uint U32(byte[] p, int o) => ((uint)p[o] << 24) | ((uint)p[o + 1] << 16) | ((uint)p[o + 2] << 8) | p[o + 3];

    // Pro testy a případné vysílání: sestaví datagram E1.31.
    public static byte[] Build(int universeNumber, byte[] dmx, int sequence, int priority = DefaultPriority, string name = "DJ Visualizer")
    {
        int len = Math.Min(dmx.Length, 512);
        var p = new byte[HeaderLength + len];
        p[1] = 0x10;
        Buffer.BlockCopy(AcnId, 0, p, 4, 12);
        SetFlagsLength(p, 16, p.Length - 16);
        p[18] = 0; p[19] = 0; p[20] = 0; p[21] = 4;
        for (int i = 0; i < 16; i++) p[22 + i] = (byte)(i + 1);            // CID
        SetFlagsLength(p, 38, p.Length - 38);
        p[40] = 0; p[41] = 0; p[42] = 0; p[43] = 2;
        var nm = Encoding.UTF8.GetBytes(name ?? "");
        Buffer.BlockCopy(nm, 0, p, 44, Math.Min(nm.Length, 63));
        p[108] = (byte)priority;
        p[111] = (byte)sequence;
        p[113] = (byte)(universeNumber >> 8); p[114] = (byte)(universeNumber & 0xFF);
        SetFlagsLength(p, 115, p.Length - 115);
        p[117] = 0x02; p[118] = 0xA1;
        p[121] = 0x00; p[122] = 0x01;
        p[123] = (byte)((len + 1) >> 8); p[124] = (byte)((len + 1) & 0xFF);
        p[125] = 0x00;
        Buffer.BlockCopy(dmx, 0, p, HeaderLength, len);
        return p;
    }

    static void SetFlagsLength(byte[] p, int o, int len)
    {
        p[o] = (byte)(0x70 | ((len >> 8) & 0x0F));
        p[o + 1] = (byte)(len & 0xFF);
    }
}

// Aktuální data všech universů. Zapisují přijímací vlákna, čte hlavní vlákno.
// Pravidla: paket se starším pořadovým číslem se zahodí (E1.31 §6.7.2: rozdíl −19…0);
// zdroj s nižší prioritou nepřepíše vyšší, dokud ten neutichne (2,5 s).
public sealed class UniverseStore
{
    public const int MaxUniverses = 32768;      // 15bitový Art-Net port-address
    public const int SourceTimeoutMs = 2500;

    readonly byte[][] data = new byte[MaxUniverses][];
    readonly int[] priority = new int[MaxUniverses];
    readonly int[] lastSeq = new int[MaxUniverses];
    readonly long[] lastMs = new long[MaxUniverses];
    readonly object gate = new object();
    long dropped, rejected;

    public long Dropped { get { lock (gate) return dropped; } }          // pozdě došlé pakety
    public long Rejected { get { lock (gate) return rejected; } }        // přebité zdrojem s vyšší prioritou

    // true = data přijata. nowMs je libovolné monotónní milisekundové hodiny.
    public bool Submit(int universe, byte[] src, int offset, int length, int sequence, int prio, long nowMs)
    {
        if (universe < 0 || universe >= MaxUniverses || length <= 0) return false;
        lock (gate)
        {
            var buf = data[universe];
            bool fresh = buf == null;
            bool stale = !fresh && nowMs - lastMs[universe] > SourceTimeoutMs;
            if (!fresh && !stale)
            {
                if (prio < priority[universe]) { rejected++; return false; }
                if (sequence != 0 && lastSeq[universe] != 0)
                {
                    int d = (sbyte)(sequence - lastSeq[universe]);
                    if (d == 0) return false;                                   // duplikát (broadcast dorazil na víc socketů)
                    if (d < 0 && d > -20) { dropped++; return false; }
                }
            }
            if (fresh) data[universe] = buf = new byte[512];
            Buffer.BlockCopy(src, offset, buf, 0, Math.Min(length, 512));
            priority[universe] = prio;
            lastSeq[universe] = sequence;
            lastMs[universe] = nowMs;
            return true;
        }
    }

    // Zkopíruje 512 kanálů do dest (nuly, když universe nikdy nepřišlo). false = neplatné číslo universa.
    public bool Get(int universe, byte[] dest)
    {
        if (universe < 0 || universe >= MaxUniverses) return false;
        lock (gate)
        {
            var buf = data[universe];
            if (buf == null) Array.Clear(dest, 0, Math.Min(512, dest.Length));
            else Buffer.BlockCopy(buf, 0, dest, 0, 512);
        }
        return true;
    }

    // Zdroj skončil stream (sACN) – universe se vynuluje a hned přijme kohokoli.
    public void Release(int universe)
    {
        if (universe < 0 || universe >= MaxUniverses) return;
        lock (gate)
        {
            if (data[universe] != null) Array.Clear(data[universe], 0, 512);
            priority[universe] = 0;
            lastSeq[universe] = 0;
        }
    }

    // Kolik universů dostalo data v posledních timeoutMs (pro stavový řádek).
    public int ActiveCount(long nowMs, int timeoutMs = SourceTimeoutMs)
    {
        int n = 0;
        lock (gate)
            for (int u = 0; u < MaxUniverses; u++)
                if (data[u] != null && nowMs - lastMs[u] <= timeoutMs) n++;
        return n;
    }
}
