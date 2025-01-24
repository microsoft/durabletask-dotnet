internal enum ScheduleState
{
    Provisioning, // Schedule is being created
    Active,       // Schedule is active and running
    Paused,       // Schedule is paused
    Failed,       // Schedule has failed
    Updating      // Schedule is being updated
}