using System.Text.Json.Serialization;

namespace RecipeManager.Core.Modules.MealPlanning.Contracts;

public enum MealType
{
    [JsonStringEnumMemberName("cook")] Cook,
    [JsonStringEnumMemberName("leftovers")] Leftovers,
    [JsonStringEnumMemberName("other")] Other
}
