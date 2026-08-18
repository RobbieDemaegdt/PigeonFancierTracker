using PigeonFancierTracker.Core.Contracts;

namespace PigeonFancierTracker.Worker;

public sealed record AdvisorReport(
    DateTime GeneratedAtUtc,
    FinanceStatus? Finance,
    FoodManagementPlan? FoodPlan,
    TrainingManagementPlan? TrainingPlan,
    FlightEnrollmentPlan? FlightPlan,
    BreedingManagementPlan? BreedingPlan,
    LoftManagementPlan? LoftPlan);
