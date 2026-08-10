using System;
using System.Threading;
using MacBookEco.Telemetry;

namespace MacBookEco.Tests.Smoke
{
    internal static class Program
    {
        internal static TestCase[] CreateCases()
        {
            return new[]
            {
                Test(
                    "Read-only telemetry capture respects dashboard visibility",
                    TestReadOnlyTelemetryCapture),
                Test(
                    "Telemetry disposal waits for active captures",
                    TestTelemetryLifecycleBarrier),
                Test(
                    "Telemetry classification preserves unavailable values",
                    TestTelemetryClassificationPolicies)
            };
        }

        private static TestCase Test(string name, Action body)
        {
            return new TestCase("Telemetry: " + name, body);
        }

        private static void TestReadOnlyTelemetryCapture()
        {
            CountingGpuTelemetryProvider gpu = new CountingGpuTelemetryProvider();
            using (TelemetryService telemetry = new TelemetryService(
                new BatteryTelemetryProvider(),
                new CpuTelemetryProvider(),
                new DisplayTelemetryProvider(),
                gpu))
            {
                TelemetrySnapshot hidden = telemetry.CaptureOnce(false);
                Check.That(gpu.CaptureCount == 0,
                    "GPU was polled while the dashboard was hidden.");
                Check.That(
                    hidden.Gpu.Availability == TelemetryAvailability.Unavailable,
                    "Hidden GPU state should report paused/unavailable.");

                telemetry.SetDashboardVisible(true);
                TelemetrySnapshot visible = telemetry.CaptureOnce(true);
                Check.That(gpu.StartCount == 1,
                    "GPU monitoring did not start exactly once.");
                Check.That(gpu.CaptureCount == 1,
                    "GPU was not polled exactly once.");
                Check.That(
                    visible.Gpu.Availability == TelemetryAvailability.Unsupported,
                    "The smoke GPU provider should report unsupported.");

                if (visible.Display.Availability == TelemetryAvailability.Available)
                {
                    Check.That(visible.Display.TargetRole == "Internal"
                        && !string.IsNullOrWhiteSpace(
                            visible.Display.TargetSignature),
                        "display sample must carry the normalized internal "
                            + "target signature");
                }

                telemetry.SetDashboardVisible(false);
                Check.That(gpu.StopCount == 1,
                    "GPU monitoring did not stop when hidden.");

                Console.WriteLine(
                    "Battery: " + hidden.Battery.Availability
                    + ", " + TelemetryText.Percent(hidden.Battery.ChargePercent)
                    + ", " + TelemetryText.Watts(hidden.Battery.DischargeWatts));
                Console.WriteLine(
                    "CPU: " + visible.Cpu.Availability
                    + ", " + TelemetryText.Percent(visible.Cpu.LoadPercent)
                    + ", " + TelemetryText.Frequency(visible.Cpu.AverageMhz));
                Console.WriteLine(
                    "Display: " + visible.Display.Availability
                    + ", " + visible.Display.Width + "x" + visible.Display.Height
                    + " @ " + TelemetryText.Refresh(visible.Display.RefreshRateHz));
            }
        }

        private static void TestTelemetryLifecycleBarrier()
        {
            BlockingBatteryTelemetryProvider battery =
                new BlockingBatteryTelemetryProvider();
            TelemetryService telemetry = new TelemetryService(
                battery,
                new FixedCpuTelemetryProvider(),
                new FixedDisplayTelemetryProvider(),
                new CountingGpuTelemetryProvider());
            Exception captureFailure = null;
            Thread captureThread = new Thread(
                delegate()
                {
                    try
                    {
                        telemetry.CaptureOnce(false);
                    }
                    catch (Exception exception)
                    {
                        captureFailure = exception;
                    }
                });
            ManualResetEvent disposeReturned = new ManualResetEvent(false);
            Thread disposeThread = null;
            try
            {
                captureThread.Start();
                Check.That(battery.Entered.WaitOne(1000),
                    "lifecycle test capture did not enter its provider");

                disposeThread = new Thread(
                    delegate()
                    {
                        telemetry.Dispose();
                        disposeReturned.Set();
                    });
                disposeThread.Start();
                Check.That(!disposeReturned.WaitOne(100),
                    "Dispose returned before the active capture completed");
                battery.Release.Set();
                Check.That(disposeReturned.WaitOne(1000),
                    "Dispose did not finish after the active capture completed");
                Check.That(captureThread.Join(1000),
                    "active capture thread did not terminate");
                Check.That(disposeThread.Join(1000),
                    "Dispose thread did not terminate");
                Check.That(captureFailure == null,
                    "capture failed unexpectedly while Dispose waited");

                Check.Throws<ObjectDisposedException>(
                    delegate { telemetry.CaptureOnce(false); },
                    "Dispose must prevent a new public capture from starting");
            }
            finally
            {
                battery.Release.Set();
                captureThread.Join(4000);
                if (disposeThread != null)
                {
                    disposeThread.Join(4000);
                }

                telemetry.Dispose();
                disposeReturned.Dispose();
                battery.Dispose();
            }
        }

        private static void TestTelemetryClassificationPolicies()
        {
            Check.That(OptionalCpuSensorProvider.IsAllowedCpuParent("/intelcpu/0"),
                "Intel CPU parent must be accepted");
            Check.That(OptionalCpuSensorProvider.IsAllowedCpuParent("/amdcpu/0"),
                "AMD CPU parent must be accepted");
            Check.That(!OptionalCpuSensorProvider.IsAllowedCpuParent("/gpu/0"),
                "GPU parent must not be treated as a CPU sensor");
            Check.That(!OptionalCpuSensorProvider.IsAllowedCpuParent("/intelgpu/0"),
                "Intel GPU parent must not be treated as a CPU sensor");
            Check.That(!OptionalCpuSensorProvider.IsAllowedCpuParent("Core #1"),
                "a sensor display name is not a CPU identity proof");

            AdlPmLogDataOutput idlePmLog = new AdlPmLogDataOutput();
            idlePmLog.Sensors = new AdlSingleSensorData[256];
            idlePmLog.Sensors[19].Supported = 1;
            idlePmLog.Sensors[19].Value = 0;
            double? idleGpuLoad = AmdAdlSession.PmLogLoadPercent(idlePmLog);
            Check.That(idleGpuLoad.HasValue && idleGpuLoad.Value == 0.0,
                "supported PMLog GPU idle 0% must remain a valid graph sample");
            idlePmLog.Sensors[19].Supported = 0;
            Check.That(!AmdAdlSession.PmLogLoadPercent(idlePmLog).HasValue,
                "unsupported PMLog GPU load must remain absent");
            idlePmLog.Sensors[19].Supported = 1;
            idlePmLog.Sensors[19].Value = 100;
            Check.That(!AmdAdlSession.PmLogLoadPercent(idlePmLog).HasValue,
                "PMLog GPU load must retain its reviewed exclusive upper bound");

            SystemPowerStatus unknownBattery = new SystemPowerStatus();
            unknownBattery.BatteryFlag = 255;
            unknownBattery.BatteryLifePercent = 255;
            unknownBattery.BatteryLifeTime = uint.MaxValue;
            BatteryTelemetry sample =
                BatteryTelemetrySemantics.FromSystemPowerStatus(unknownBattery);
            Check.That(!sample.Charging.HasValue,
                "BatteryFlag=255 must remain charging-unknown");
            Check.That(!sample.DischargeWatts.HasValue && !sample.ChargeWatts.HasValue,
                "basic AC/battery state must not invent a system draw or charge rate");
        }

        private sealed class CountingGpuTelemetryProvider : IGpuTelemetryProvider
        {
            public int StartCount { get; private set; }

            public int CaptureCount { get; private set; }

            public int StopCount { get; private set; }

            public void StartMonitoring()
            {
                StartCount++;
            }

            public GpuTelemetry Capture()
            {
                CaptureCount++;
                return GpuTelemetry.Unsupported("Synthetic smoke-test provider.");
            }

            public void StopMonitoring()
            {
                StopCount++;
            }
        }

        private sealed class BlockingBatteryTelemetryProvider :
            IBatteryTelemetryProvider,
            IDisposable
        {
            public readonly ManualResetEvent Entered = new ManualResetEvent(false);
            public readonly ManualResetEvent Release = new ManualResetEvent(false);

            public BatteryTelemetry Capture()
            {
                Entered.Set();
                Release.WaitOne(3000);
                return BatteryTelemetry.Unavailable("Synthetic lifecycle battery.");
            }

            public void Dispose()
            {
                Entered.Dispose();
                Release.Dispose();
            }
        }

        private sealed class FixedCpuTelemetryProvider : ICpuTelemetryProvider
        {
            public CpuTelemetry Capture()
            {
                return CpuTelemetry.Paused();
            }
        }

        private sealed class FixedDisplayTelemetryProvider : IDisplayTelemetryProvider
        {
            public DisplayTelemetry Capture()
            {
                return DisplayTelemetry.Unavailable("Synthetic lifecycle display.");
            }
        }
    }
}
