namespace RbxManager.Infrastructure.Tests.Sessions;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using RbxManager.Core.Models;
using RbxManager.Core.Processes;
using RbxManager.Core.Sessions;
using RbxManager.Core.Settings;
using RbxManager.Infrastructure.Sessions;

public sealed class InstanceWatchdogServiceTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-25T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly ISessionTracker _tracker = Substitute.For<ISessionTracker>();
    private readonly IProcessProvider _processes = Substitute.For<IProcessProvider>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();

    private InstanceWatchdogService CreateWatchdog(bool autoReconnect, params RunningSession[] sessions)
    {
        _settings.CurrentSettings.Returns(AppSettings.Default with { EnableAutoReconnect = autoReconnect });
        _tracker.ActiveSessions.Returns(new List<RunningSession>(sessions));
        return new InstanceWatchdogService(_tracker, _processes, _settings, _time);
    }

    private RunningSession Session(int pid, TimeSpan age) =>
        new(new AccountId(pid), new LaunchRequest(new AccountId(pid), 1818), pid, _time.GetUtcNow() - age);

    [Fact]
    public void HungTwoPollsInARow_RecoversSession()
    {
        InstanceWatchdogService watchdog = CreateWatchdog(true, Session(100, TimeSpan.FromMinutes(5)));
        _processes.IsResponding(100).Returns(false);

        watchdog.CheckSessions();
        _tracker.DidNotReceiveWithAnyArgs().RecoverSession(default, default!);

        watchdog.CheckSessions();
        _tracker.Received(1).RecoverSession(100, "NotResponding");
    }

    [Fact]
    public void SingleHitch_ThenResponding_ResetsStrikes()
    {
        InstanceWatchdogService watchdog = CreateWatchdog(true, Session(100, TimeSpan.FromMinutes(5)));
        _processes.IsResponding(100).Returns(false, true, false);

        watchdog.CheckSessions();
        watchdog.CheckSessions();
        watchdog.CheckSessions();

        _tracker.DidNotReceiveWithAnyArgs().RecoverSession(default, default!);
    }

    [Fact]
    public void SessionInsideStartupGrace_IsNeverRecovered()
    {
        InstanceWatchdogService watchdog = CreateWatchdog(true, Session(100, TimeSpan.FromSeconds(30)));
        _processes.IsResponding(100).Returns(false);

        watchdog.CheckSessions();
        watchdog.CheckSessions();

        _tracker.DidNotReceiveWithAnyArgs().RecoverSession(default, default!);
    }

    [Fact]
    public void AutoReconnectOff_DoesNotProbeOrRecover()
    {
        InstanceWatchdogService watchdog = CreateWatchdog(false, Session(100, TimeSpan.FromMinutes(5)));

        watchdog.CheckSessions();
        watchdog.CheckSessions();

        _processes.DidNotReceiveWithAnyArgs().IsResponding(default);
        _tracker.DidNotReceiveWithAnyArgs().RecoverSession(default, default!);
    }
}
