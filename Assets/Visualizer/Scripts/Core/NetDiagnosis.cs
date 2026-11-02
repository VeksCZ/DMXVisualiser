// Proč nechodí data? Čistá logika bez Unity (má unit testy). Vstupem je stav přijímače, výstupem nejpravděpodobnější příčina.
public enum NetIssue
{
    Ok,             // data chodí
    PortBusy,       // nepodařilo se otevřít žádný socket (port 6454 drží jiný program)
    NoInterface,    // počítač nemá aktivní síťové rozhraní
    Waiting,        // právě jsme se spustili, zatím se nic nestalo
    NoTraffic,      // nepřišel ani ArtPoll, ani data → firewall / jiná síť / SoundSwitch neběží
    PolledNoData,   // SoundSwitch aplikaci vidí (ArtPoll), ale DMX neposílá → není přiřazené universe / výstup vypnutý
    Stopped,        // data chodila a přestala
}

public struct NetState
{
    public bool hasDataNow;
    public int listeners;           // kolik socketů se podařilo otevřít
    public int interfaces;          // počet síťových rozhraní; -1 = nevíme (Quest)
    public int polls;               // přijatých ArtPoll od startu
    public int packets;             // přijatých DMX paketů od startu
    public float secondsSinceStart;
}

public static class NetDiagnosis
{
    public const float WaitSeconds = 10f;

    public static NetIssue Evaluate(NetState s)
    {
        if (s.hasDataNow) return NetIssue.Ok;
        if (s.listeners <= 0) return NetIssue.PortBusy;
        if (s.interfaces == 0) return NetIssue.NoInterface;
        if (s.packets > 0) return NetIssue.Stopped;
        if (s.polls > 0) return NetIssue.PolledNoData;
        if (s.secondsSinceStart < WaitSeconds) return NetIssue.Waiting;
        return NetIssue.NoTraffic;
    }
}
