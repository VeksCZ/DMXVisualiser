using System;
using System.Collections.Generic;
using System.Net;
using Xunit;

public class ArtNetTests
{
    static byte[] ArtDmx(int universe, int seq, byte[] dmx, int declaredLen = -1)
    {
        int len = declaredLen >= 0 ? declaredLen : dmx.Length;
        var p = new byte[18 + dmx.Length];
        System.Text.Encoding.ASCII.GetBytes("Art-Net\0").CopyTo(p, 0);
        p[8] = 0x00; p[9] = 0x50; p[11] = 14;
        p[12] = (byte)seq;
        p[14] = (byte)(universe & 0xFF);
        p[15] = (byte)((universe >> 8) & 0x7F);
        p[16] = (byte)(len >> 8); p[17] = (byte)(len & 0xFF);
        dmx.CopyTo(p, 18);
        return p;
    }

    [Fact]
    public void ParsesUniverseSequenceAndLength()
    {
        var p = ArtDmx(5, 42, new byte[] { 1, 2, 3, 4 });
        Assert.True(ArtNetProtocol.TryParseDmx(p, p.Length, out var r));
        Assert.Equal(5, r.universe);
        Assert.Equal(42, r.sequence);
        Assert.Equal(18, r.offset);
        Assert.Equal(4, r.length);
        Assert.Equal(ArtNetProtocol.DefaultPriority, r.priority);
    }

    [Fact]
    public void UsesFull15BitPortAddress()
    {
        // dřív se universe maskovalo na 4 bity: universe 16 přepisovalo universe 0
        var p = ArtDmx(0x1234, 1, new byte[] { 9 });
        Assert.True(ArtNetProtocol.TryParseDmx(p, p.Length, out var r));
        Assert.Equal(0x1234, r.universe);
        p = ArtDmx(16, 1, new byte[] { 9 });
        Assert.True(ArtNetProtocol.TryParseDmx(p, p.Length, out r));
        Assert.Equal(16, r.universe);
    }

    [Fact]
    public void ClampsDeclaredLengthToPacketAnd512()
    {
        var p = ArtDmx(0, 0, new byte[10], declaredLen: 512);
        Assert.True(ArtNetProtocol.TryParseDmx(p, p.Length, out var r));
        Assert.Equal(10, r.length);
        var big = ArtDmx(0, 0, new byte[600], declaredLen: 600);
        Assert.True(ArtNetProtocol.TryParseDmx(big, big.Length, out r));
        Assert.Equal(512, r.length);
    }

    [Fact]
    public void RejectsGarbage()
    {
        Assert.False(ArtNetProtocol.TryParseDmx(new byte[5], 5, out _));
        Assert.False(ArtNetProtocol.TryParseDmx(new byte[40], 40, out _));
        var p = ArtDmx(0, 0, new byte[4]);
        p[0] = (byte)'X';
        Assert.False(ArtNetProtocol.TryParseDmx(p, p.Length, out _));
        Assert.Equal(-1, ArtNetProtocol.OpCode(p, p.Length));
        Assert.False(ArtNetProtocol.TryParseDmx(ArtDmx(0, 0, new byte[0]), 18, out _));   // nulová délka
    }

    [Fact]
    public void RecognisesPoll()
    {
        var p = new byte[14];
        System.Text.Encoding.ASCII.GetBytes("Art-Net\0").CopyTo(p, 0);
        p[9] = 0x20;
        Assert.Equal(ArtNetProtocol.OpPoll, ArtNetProtocol.OpCode(p, p.Length));
    }

    static readonly byte[] Ip = { 192, 168, 1, 50 };

    [Fact]
    public void PollReplyDefaultsToUniverses0To3()
    {
        var replies = ArtNetProtocol.BuildPollReplies(Ip, "DJ Visualizer", null);
        Assert.Single(replies);
        var r = replies[0];
        Assert.Equal(239, r.Length);
        Assert.Equal(0x00, r[8]); Assert.Equal(0x21, r[9]);
        Assert.Equal(Ip, new[] { r[10], r[11], r[12], r[13] });
        Assert.Equal(4, r[173]);
        for (int i = 0; i < 4; i++) Assert.Equal(i, r[190 + i]);
        Assert.Equal(1, r[211]);
        Assert.Equal("DJ Visualizer", System.Text.Encoding.ASCII.GetString(r, 26, 13));
    }

    [Fact]
    public void PollReplyAdvertisesUsedUniversesInGroupsOfFour()
    {
        // 0-3 (výchozí) + 5 + 20 + 300 → skupiny podle Net/SubNet (u >> 4)
        var replies = ArtNetProtocol.BuildPollReplies(Ip, "DJ Visualizer", new[] { 5, 20, 300 });
        // sub 0: 0,1,2,3 | sub 0: 5 | sub 1: 20 | net 1 sub 2: 300
        Assert.Equal(4, replies.Count);
        Assert.Equal(1, replies[0][211]); Assert.Equal(2, replies[1][211]);
        Assert.Equal(4, replies[0][173]); Assert.Equal(1, replies[1][173]);
        Assert.Equal(5, replies[1][190]);
        Assert.Equal(1, replies[2][19]);  Assert.Equal(20 & 0xF, replies[2][190]);
        Assert.Equal(1, replies[3][18]);  Assert.Equal((300 >> 4) & 0xF, replies[3][19]); Assert.Equal(300 & 0xF, replies[3][190]);
    }

    [Fact]
    public void PollReplyIgnoresInvalidUniverses()
    {
        var replies = ArtNetProtocol.BuildPollReplies(Ip, "x", new[] { -1, 99999 });
        Assert.Single(replies);
    }
}

public class SacnTests
{
    [Fact]
    public void MulticastAddress()
    {
        Assert.Equal(IPAddress.Parse("239.255.0.1"), SacnProtocol.MulticastAddress(1));
        Assert.Equal(IPAddress.Parse("239.255.1.44"), SacnProtocol.MulticastAddress(300));
    }

    [Fact]
    public void RoundTrip()
    {
        var dmx = new byte[512];
        for (int i = 0; i < 512; i++) dmx[i] = (byte)i;
        var p = SacnProtocol.Build(3, dmx, 77, 120, "Test Source");
        Assert.True(SacnProtocol.TryParse(p, p.Length, out var r, out var name));
        Assert.Equal(2, r.universe);            // universe 3 podle E1.31 = index 2
        Assert.Equal(77, r.sequence);
        Assert.Equal(120, r.priority);
        Assert.Equal(512, r.length);
        Assert.Equal("Test Source", name);
        Assert.False(r.terminated);
        Assert.Equal(dmx[200], p[r.offset + 200]);
    }

    [Fact]
    public void ShortFrame()
    {
        var p = SacnProtocol.Build(1, new byte[] { 255, 128 }, 1);
        Assert.True(SacnProtocol.TryParse(p, p.Length, out var r, out _));
        Assert.Equal(2, r.length);
        Assert.Equal(0, r.universe);
    }

    [Fact]
    public void StreamTerminated()
    {
        var p = SacnProtocol.Build(1, new byte[4], 1);
        p[112] |= 0x40;
        Assert.True(SacnProtocol.TryParse(p, p.Length, out var r, out _));
        Assert.True(r.terminated);
    }

    [Fact]
    public void RejectsOtherStartCodesAndGarbage()
    {
        var p = SacnProtocol.Build(1, new byte[4], 1);
        p[125] = 0xDD;                                          // alternativní start code
        Assert.False(SacnProtocol.TryParse(p, p.Length, out _, out _));

        p = SacnProtocol.Build(1, new byte[4], 1);
        p[5] = 0;                                               // špatné ACN ID
        Assert.False(SacnProtocol.TryParse(p, p.Length, out _, out _));

        p = SacnProtocol.Build(1, new byte[4], 1);
        p[43] = 8;                                              // jiný framing vector (sync/discovery)
        Assert.False(SacnProtocol.TryParse(p, p.Length, out _, out _));

        Assert.False(SacnProtocol.TryParse(new byte[100], 100, out _, out _));
        Assert.False(SacnProtocol.TryParse(null, 0, out _, out _));
    }

    [Fact]
    public void RejectsUniverseZero()
    {
        var p = SacnProtocol.Build(0, new byte[4], 1);
        Assert.False(SacnProtocol.TryParse(p, p.Length, out _, out _));
    }

    [Fact]
    public void SizeFieldBeyondDatagramIsClamped()
    {
        var p = SacnProtocol.Build(1, new byte[100], 1);
        p[123] = 0x02; p[124] = 0x01;                           // tvrdí 513 hodnot
        Assert.True(SacnProtocol.TryParse(p, p.Length, out var r, out _));
        Assert.Equal(100, r.length);
    }
}

public class UniverseStoreTests
{
    static byte[] Fill(byte v, int n = 512) { var b = new byte[n]; Array.Fill(b, v); return b; }

    [Fact]
    public void StoresAndReturnsData()
    {
        var s = new UniverseStore();
        Assert.True(s.Submit(0, Fill(7), 0, 512, 1, 100, 0));
        var d = new byte[512];
        Assert.True(s.Get(0, d));
        Assert.Equal(7, d[0]); Assert.Equal(7, d[511]);
    }

    [Fact]
    public void UnseenUniverseIsZeroAndInvalidIsRejected()
    {
        var s = new UniverseStore();
        var d = Fill(9);
        Assert.True(s.Get(3, d));
        Assert.All(d, b => Assert.Equal(0, b));
        Assert.False(s.Get(-1, d));
        Assert.False(s.Get(UniverseStore.MaxUniverses, d));
        Assert.False(s.Submit(UniverseStore.MaxUniverses, Fill(1), 0, 512, 1, 100, 0));
    }

    [Fact]
    public void Universe16DoesNotAliasUniverse0()
    {
        var s = new UniverseStore();
        s.Submit(0, Fill(1), 0, 512, 0, 100, 0);
        s.Submit(16, Fill(2), 0, 512, 0, 100, 0);
        var d = new byte[512];
        s.Get(0, d); Assert.Equal(1, d[0]);
        s.Get(16, d); Assert.Equal(2, d[0]);
    }

    [Fact]
    public void ShortFrameKeepsRestOfUniverse()
    {
        var s = new UniverseStore();
        s.Submit(0, Fill(5), 0, 512, 0, 100, 0);
        s.Submit(0, Fill(9, 4), 0, 4, 0, 100, 1);
        var d = new byte[512];
        s.Get(0, d);
        Assert.Equal(9, d[3]); Assert.Equal(5, d[4]);
    }

    [Fact]
    public void HonoursOffset()
    {
        var s = new UniverseStore();
        var pkt = new byte[18 + 3];
        pkt[18] = 1; pkt[19] = 2; pkt[20] = 3;
        s.Submit(0, pkt, 18, 3, 0, 100, 0);
        var d = new byte[512];
        s.Get(0, d);
        Assert.Equal(new byte[] { 1, 2, 3, 0 }, new[] { d[0], d[1], d[2], d[3] });
    }

    [Fact]
    public void OutOfOrderPacketsAreDroppedAndCounted()
    {
        var s = new UniverseStore();
        Assert.True(s.Submit(0, Fill(1), 0, 512, 10, 100, 0));
        Assert.True(s.Submit(0, Fill(2), 0, 512, 11, 100, 10));
        Assert.False(s.Submit(0, Fill(3), 0, 512, 10, 100, 20));     // pozdě
        Assert.Equal(1, s.Dropped);
        var d = new byte[512]; s.Get(0, d);
        Assert.Equal(2, d[0]);
    }

    [Fact]
    public void DuplicateIsIgnoredWithoutCountingAsDrop()
    {
        var s = new UniverseStore();
        s.Submit(0, Fill(1), 0, 512, 10, 100, 0);
        Assert.False(s.Submit(0, Fill(1), 0, 512, 10, 100, 1));
        Assert.Equal(0, s.Dropped);
    }

    [Fact]
    public void SequenceWrapAroundIsAccepted()
    {
        var s = new UniverseStore();
        s.Submit(0, Fill(1), 0, 512, 255, 100, 0);
        Assert.True(s.Submit(0, Fill(2), 0, 512, 1, 100, 10));       // 255 → 1 přes přetečení
        Assert.Equal(0, s.Dropped);
    }

    [Fact]
    public void SequenceZeroDisablesChecking()
    {
        var s = new UniverseStore();
        s.Submit(0, Fill(1), 0, 512, 50, 100, 0);
        Assert.True(s.Submit(0, Fill(2), 0, 512, 0, 100, 10));
        Assert.True(s.Submit(0, Fill(3), 0, 512, 0, 100, 20));
    }

    [Fact]
    public void SenderRestartRecoversAfterTimeout()
    {
        var s = new UniverseStore();
        s.Submit(0, Fill(1), 0, 512, 200, 100, 0);
        Assert.True(s.Submit(0, Fill(2), 0, 512, 5, 100, UniverseStore.SourceTimeoutMs + 1));   // odešel a vrátil se od 5
    }

    [Fact]
    public void HigherPriorityWinsUntilSourceGoesQuiet()
    {
        var s = new UniverseStore();
        s.Submit(0, Fill(1), 0, 512, 1, 150, 0);
        Assert.False(s.Submit(0, Fill(2), 0, 512, 1, 100, 100));
        Assert.Equal(1, s.Rejected);
        var d = new byte[512]; s.Get(0, d); Assert.Equal(1, d[0]);
        Assert.True(s.Submit(0, Fill(3), 0, 512, 1, 100, 100 + UniverseStore.SourceTimeoutMs + 1));
        s.Get(0, d); Assert.Equal(3, d[0]);
    }

    [Fact]
    public void EqualPriorityLastWriterWins()
    {
        var s = new UniverseStore();
        s.Submit(0, Fill(1), 0, 512, 0, 100, 0);
        Assert.True(s.Submit(0, Fill(2), 0, 512, 0, 100, 1));
    }

    [Fact]
    public void ReleaseBlanksUniverseAndAcceptsAnyone()
    {
        var s = new UniverseStore();
        s.Submit(0, Fill(8), 0, 512, 30, 200, 0);
        s.Release(0);
        var d = new byte[512]; s.Get(0, d); Assert.Equal(0, d[0]);
        Assert.True(s.Submit(0, Fill(4), 0, 512, 1, 10, 1));
    }

    [Fact]
    public void ActiveCountCountsRecentUniversesOnly()
    {
        var s = new UniverseStore();
        s.Submit(0, Fill(1), 0, 512, 0, 100, 0);
        s.Submit(1, Fill(1), 0, 512, 0, 100, 5000);
        Assert.Equal(1, s.ActiveCount(5100));
        Assert.Equal(2, s.ActiveCount(5100, 10000));
    }

    [Fact]
    public void IsActiveReflectsRecentData()
    {
        var s = new UniverseStore();
        Assert.False(s.IsActive(0, 0));
        s.Submit(0, Fill(1), 0, 512, 0, 100, 1000);
        Assert.True(s.IsActive(0, 1500));
        Assert.False(s.IsActive(0, 1000 + UniverseStore.SourceTimeoutMs + 1));
        Assert.False(s.IsActive(-1, 0));
        Assert.False(s.IsActive(1, 1500));
    }

    [Fact]
    public async System.Threading.Tasks.Task ThreadSafetyStress()
    {
        var s = new UniverseStore();
        var tasks = new List<System.Threading.Tasks.Task>();
        for (int t = 0; t < 4; t++)
        {
            int id = t;
            tasks.Add(System.Threading.Tasks.Task.Run(() =>
            {
                var d = new byte[512];
                for (int i = 0; i < 5000; i++)
                {
                    s.Submit(id, Fill((byte)id), 0, 512, 0, 100, i);
                    s.Get(id, d);
                    Assert.All(d, b => Assert.Equal((byte)id, b));      // nikdy napůl přepsaný rámec
                }
            }));
        }
        await System.Threading.Tasks.Task.WhenAll(tasks);
    }
}
