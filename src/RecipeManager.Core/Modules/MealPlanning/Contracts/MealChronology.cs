namespace RecipeManager.Core.Modules.MealPlanning.Contracts;

public static class MealChronology
{
    public static bool IsLater(DateOnly sourceDate, int sourcePosition, DateOnly mealDate, int mealPosition)
        => mealDate > sourceDate || (mealDate == sourceDate && mealPosition > sourcePosition);
}
