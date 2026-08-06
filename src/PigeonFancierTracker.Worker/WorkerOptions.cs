namespace PigeonFancierTracker.Worker;

public sealed class WorkerOptions
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public int FancierId { get; set; }
    public int SyncIntervalMinutes { get; set; } = 30;
    public bool AutoBidEnabled { get; set; }
    public List<AutoBidRule> AutoBidRules { get; set; } = [];

    public bool ManagementEnabled { get; set; }
    public bool AutoFlightEnabled { get; set; } = true;
    public bool AutoFeedEnabled { get; set; } = true;
    public int MinFoodDaysReserve { get; set; } = 7;
    public bool AutoFinanceGuardEnabled { get; set; } = true;
    public decimal MinBalanceAlert { get; set; } = 1000m;
    public bool AutoTrainEnabled { get; set; } = true;
    public bool AutoLoftEnabled { get; set; } = true;
    public bool AutoBreedEnabled { get; set; }
    public bool DryRun { get; set; }
}

public sealed class AutoBidRule
{
    public int TransferId { get; set; }
    public string PigeonName { get; set; } = "";
    public decimal MaxPrice { get; set; }
}
