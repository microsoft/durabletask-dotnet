namespace WebAPI.Orchestrations
{
    using DurableTask;
    using WebAPI.Models;

    [DurableTask("CheckInventory")]
    public class CheckInventoryActivity : TaskActivityBase<OrderInfo, bool>
    {
        readonly ILogger logger;

        // Dependencies are injected from ASP.NET host service container
        public CheckInventoryActivity(ILogger<CheckInventoryActivity> logger)
        {
            this.logger = logger;
        }

        protected override bool OnRun(OrderInfo? orderInfo)
        {
            if (orderInfo == null)
            {
                throw new ArgumentException("Failed to read order info!");
            }

            this.logger.LogInformation(
                "{instanceId}: Checking inventory for '{item}'...found some!",
                this.Context.InstanceId,
                orderInfo.Item);
            return true;
        }
    }
}
