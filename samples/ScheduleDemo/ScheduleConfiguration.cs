class ScheduleConfiguration
{
    public ScheduleConfiguration(string orchestrationName, string scheduleId)
    {
        this.orchestrationName = Check.NotNullOrEmpty(orchestrationName, nameof(orchestrationName));
        this.ScheduleId = scheduleId ?? Guid.NewGuid().ToString("N");
        this.Version++;
    }

    string orchestrationName;

    public string OrchestrationName
    {
        get => this.orchestrationName;
        set
        {
            this.orchestrationName = Check.NotNullOrEmpty(value, nameof(value));
        }
    }

    string scheduleId;

    public string ScheduleId
    {
        get => this.scheduleId;
        set
        {
            this.scheduleId = Check.NotNullOrEmpty(value, nameof(value));
        }
    }

    public string? OrchestrationInput { get; set; }

    public string? OrchestrationInstanceId { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset? StartAt { get; set; }

    public DateTimeOffset? EndAt { get; set; }

    TimeSpan? interval;

    public TimeSpan? Interval
    {
        get => this.interval;
        set
        {
            if (!value.HasValue)
            {
                return;
            }

            if (value.Value <= TimeSpan.Zero)
            {
                throw new ArgumentException("Interval must be positive", nameof(value));
            }

            if (value.Value.TotalSeconds < 1)
            {
                throw new ArgumentException("Interval must be at least 1 second", nameof(value));
            }

            this.interval = value;
        }
    }

    public string? CronExpression { get; set; }

    public int MaxOccurrence { get; set; }

    public bool? StartImmediatelyIfLate { get; set; }

    internal int Version { get; set; } // Tracking schedule config version
}