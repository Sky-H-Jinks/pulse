namespace ControlPlane.Api.Scheduling;

public class SchedulerOptions
{
    public bool Enabled { get; set; } = true;
    public int PeriodSeconds { get; set; } = 5;
    public int MaxConcurrency { get; set; } = 10;
}
