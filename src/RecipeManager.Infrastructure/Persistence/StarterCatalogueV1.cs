namespace RecipeManager.Infrastructure.Persistence;

// Versioned starter data: keep V1 stable because its migration uses the same definitions.
public static class StarterCatalogueV1
{
    public static readonly string[] Categories = ["Produce", "Meat", "Dairy", "Pantry"];
    public static readonly (string Category, string[] Names)[] Ingredients =
    [
        ("Produce", ["Apple", "Banana", "Lemon", "Lime", "Orange", "Tomato", "Potato", "Sweet potato", "Onion", "Red onion", "Garlic", "Carrot", "Celery", "Broccoli", "Cauliflower", "Capsicum", "Zucchini", "Cucumber", "Lettuce", "Spinach", "Mushroom", "Avocado", "Ginger", "Spring onion", "Basil", "Parsley", "Coriander", "Mint"]),
        ("Meat", ["Chicken breast", "Chicken thigh", "Beef mince", "Beef steak", "Pork mince", "Bacon", "Salmon", "Tuna", "Prawns"]),
        ("Dairy", ["Milk", "Butter", "Cream", "Cheddar cheese", "Parmesan", "Greek yoghurt", "Egg"]),
        ("Pantry", ["Rice", "Pasta", "Plain flour", "Self-raising flour", "Rolled oats", "Bread", "Sugar", "Brown sugar", "Honey", "Olive oil", "Vegetable oil", "Salt", "Black pepper", "Paprika", "Cumin", "Cinnamon", "Dried oregano", "Chilli flakes", "Baking powder", "Bicarbonate of soda", "Vanilla extract", "Soy sauce", "Vinegar", "Tomato paste", "Canned tomatoes", "Coconut milk", "Chickpeas", "Lentils", "Chicken stock", "Vegetable stock", "Peanut butter"])
    ];
    public static readonly string[] Collections = ["Breakfast", "Lunch", "Dinner", "Snacks", "Desserts", "Sides"];
    public static readonly string[] Tags = ["Quick and easy", "Vegetarian", "Vegan", "Meal prep", "Family favourites", "One pot"];
}
