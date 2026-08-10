namespace MacBookEco.Telemetry
{
    public interface IBatteryTelemetryProvider
    {
        BatteryTelemetry Capture();
    }

    public interface ICpuTelemetryProvider
    {
        CpuTelemetry Capture();
    }

    public interface IDisplayTelemetryProvider
    {
        DisplayTelemetry Capture();
    }

    public interface IGpuTelemetryProvider
    {
        void StartMonitoring();

        GpuTelemetry Capture();

        void StopMonitoring();
    }

}
