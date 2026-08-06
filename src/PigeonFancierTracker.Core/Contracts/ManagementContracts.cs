using System.Text.Json.Serialization;

namespace PigeonFancierTracker.Core.Contracts;

public sealed record FlightSubscriptionsResponse(
    [property: JsonPropertyName("subscriptions")] FlightSubscriptionEntry[]? Subscriptions,
    [property: JsonPropertyName("eligible")] FlightEligibleEntry[]? Eligible);

public sealed record FlightSubscriptionEntry(
    [property: JsonPropertyName("pigeonId")] int PigeonId);

public sealed record FlightEligibleEntry(
    [property: JsonPropertyName("pigeonId")] int PigeonId,
    [property: JsonPropertyName("coupleId")] int? CoupleId,
    [property: JsonPropertyName("status")] string? Status);

public sealed record FlightSubscriptionRequest(
    [property: JsonPropertyName("add")] int[] Add,
    [property: JsonPropertyName("remove")] int[] Remove);

public sealed record FlightEnrollmentAction(
    int FlightId,
    string FlightType,
    int DistanceKm,
    string DistanceCategory,
    DateTime FlightStart,
    string? Location,
    int PigeonId,
    string PigeonName,
    double Score,
    string Reason);

public sealed record FlightEnrollmentPlan(
    IReadOnlyList<FlightEnrollmentAction> Actions,
    IReadOnlyList<string> Skipped);

public sealed record PigeonAvailability(
    int PigeonId,
    string Name,
    bool IsSick,
    bool IsFlying,
    bool IsBreeding,
    int FlightsThisWeek,
    bool HasEnergy);

public interface IFlightManager
{
    Task<FlightEnrollmentPlan> BuildEnrollmentPlanAsync(
        int fancierId,
        CancellationToken cancellationToken = default);

    Task<int> ExecuteEnrollmentAsync(
        FlightEnrollmentPlan plan,
        CancellationToken cancellationToken = default);
}

// --- Food Management ---

public sealed record FancierInventoryItemDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("unitPrice")] decimal UnitPrice,
    [property: JsonPropertyName("stock")] decimal Stock);

public sealed record FoodPurchaseAction(
    int ItemId,
    string Name,
    int Amount,
    decimal UnitPrice,
    decimal TotalCost);

public sealed record FoodDistributionAction(
    int Barley,
    int Grain,
    int Corn,
    int Peanut);

public sealed record FoodManagementPlan(
    IReadOnlyList<FoodPurchaseAction> Purchases,
    FoodDistributionAction? DistributionChange,
    double DaysRemaining,
    IReadOnlyList<string> Skipped);

public sealed record FoodPurchaseRequestItem(
    [property: JsonPropertyName("amount")] int Amount,
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("unitPrice")] decimal UnitPrice,
    [property: JsonPropertyName("stock")] decimal Stock);

public sealed record FoodPurchaseRequest(
    [property: JsonPropertyName("items")] IReadOnlyList<FoodPurchaseRequestItem> Items);

public sealed record FoodDistributionRequest(
    [property: JsonPropertyName("barley")] int Barley,
    [property: JsonPropertyName("grain")] int Grain,
    [property: JsonPropertyName("corn")] int Corn,
    [property: JsonPropertyName("peanut")] int Peanut);

// --- Finance Guard ---

public enum FinanceAlertLevel { None, Low, Critical, Bankrupt }

public sealed record FinanceStatus(
    decimal Balance,
    decimal PreviousBalance,
    decimal BalanceDelta,
    decimal TransferBalance,
    decimal Savings,
    FinanceAlertLevel AlertLevel,
    bool SpendingAllowed,
    IReadOnlyList<string> Warnings);

public interface IFinanceGuard
{
    Task<FinanceStatus> EvaluateAsync(
        int fancierId,
        decimal minBalanceAlert,
        CancellationToken cancellationToken = default);
}

// --- Food Management ---

public interface IFoodManager
{
    Task<FoodManagementPlan> BuildFoodPlanAsync(
        int fancierId,
        int minDaysReserve,
        CancellationToken cancellationToken = default);

    Task ExecuteFoodPlanAsync(
        FoodManagementPlan plan,
        CancellationToken cancellationToken = default);
}

// --- Training Management ---

public enum TrainingFocus { General, Conditional, Strategic }

public sealed record TrainingRecommendation(
    TrainingFocus RecommendedFocus,
    double Score,
    string Reason);

public sealed record TrainingManagementPlan(
    TrainingFocus? CurrentFocus,
    TrainingRecommendation Recommendation,
    IReadOnlyList<string> SkillAnalysis,
    IReadOnlyList<string> Skipped);

public interface ITrainingManager
{
    Task<TrainingManagementPlan> BuildTrainingPlanAsync(
        int fancierId,
        CancellationToken cancellationToken = default);

    Task ExecuteTrainingPlanAsync(
        TrainingManagementPlan plan,
        CancellationToken cancellationToken = default);
}

// --- Loft Management ---

public enum LoftTier
{
    RunDown = 0,
    Loft = 1,
    PigeonVilla = 2,
    PigeonComplex = 3,
    PigeonParadise = 4,
}

public sealed record LoftCleanAction(
    int CurrentDirt,
    string Reason);

public sealed record LoftPenPurchaseAction(
    int ItemId,
    string Name,
    int Amount,
    decimal UnitPrice,
    decimal TotalCost);

public sealed record LoftManagementPlan(
    LoftCleanAction? CleanAction,
    LoftPenPurchaseAction? PenPurchaseAction,
    int CurrentCapacity,
    int CurrentOccupied,
    double OccupancyPercent,
    int? CurrentDirt,
    IReadOnlyList<string> Skipped);

public interface ILoftManager
{
    Task<LoftManagementPlan> BuildLoftPlanAsync(
        int fancierId,
        CancellationToken cancellationToken = default);

    Task ExecuteLoftPlanAsync(
        LoftManagementPlan plan,
        CancellationToken cancellationToken = default);
}

// --- Breeding Management ---

public sealed record CreateCoupleRequest(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("cockId")] int CockId,
    [property: JsonPropertyName("henId")] int HenId);

public sealed record BreedingCandidate(
    int CockId,
    string CockName,
    decimal CockLibido,
    decimal CockTotalSkill,
    int HenId,
    string HenName,
    decimal HenLibido,
    decimal HenTotalSkill,
    double CompatibilityScore,
    string Reason);

public sealed record IncompatibleCouple(
    int CoupleId,
    int CockId,
    string CockName,
    int HenId,
    string HenName,
    int Days,
    string Reason);

public sealed record BreedingManagementPlan(
    IReadOnlyList<BreedingCandidate> PairsToCreate,
    IReadOnlyList<IncompatibleCouple> PairsToSplit,
    int CurrentCoupleCount,
    int AvailableBreedingSlots,
    IReadOnlyList<string> Skipped);

public interface IBreedingManager
{
    Task<BreedingManagementPlan> BuildBreedingPlanAsync(
        int fancierId,
        CancellationToken cancellationToken = default);

    Task ExecuteBreedingPlanAsync(
        BreedingManagementPlan plan,
        CancellationToken cancellationToken = default);
}
