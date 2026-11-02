using Xunit;

public class NetDiagnosisTests
{
    static NetState S(bool data = false, int listeners = 1, int ifaces = 1, int polls = 0, int packets = 0, float t = 60f) =>
        new NetState { hasDataNow = data, listeners = listeners, interfaces = ifaces, polls = polls, packets = packets, secondsSinceStart = t };

    [Fact] public void DataFlowingIsOk() => Assert.Equal(NetIssue.Ok, NetDiagnosis.Evaluate(S(data: true, packets: 100)));
    [Fact] public void NoSocketMeansPortBusy() => Assert.Equal(NetIssue.PortBusy, NetDiagnosis.Evaluate(S(listeners: 0)));
    [Fact] public void NoInterface() => Assert.Equal(NetIssue.NoInterface, NetDiagnosis.Evaluate(S(ifaces: 0)));
    [Fact] public void UnknownInterfaceCountIsNotAProblem() => Assert.Equal(NetIssue.NoTraffic, NetDiagnosis.Evaluate(S(ifaces: -1)));
    [Fact] public void RightAfterStartWeWait() => Assert.Equal(NetIssue.Waiting, NetDiagnosis.Evaluate(S(t: 3f)));
    [Fact] public void SilenceForLongIsNoTraffic() => Assert.Equal(NetIssue.NoTraffic, NetDiagnosis.Evaluate(S(t: 30f)));
    [Fact] public void PollWithoutDataPointsToSoundSwitchSetup() => Assert.Equal(NetIssue.PolledNoData, NetDiagnosis.Evaluate(S(polls: 3)));
    [Fact] public void PollWithoutDataStillWaitsNothing() => Assert.Equal(NetIssue.PolledNoData, NetDiagnosis.Evaluate(S(polls: 1, t: 2f)));
    [Fact] public void DataThatStoppedIsReported() => Assert.Equal(NetIssue.Stopped, NetDiagnosis.Evaluate(S(packets: 500, polls: 2)));
    [Fact] public void PortBusyBeatsEverythingButData() => Assert.Equal(NetIssue.PortBusy, NetDiagnosis.Evaluate(S(listeners: 0, ifaces: 0, packets: 5)));
}
