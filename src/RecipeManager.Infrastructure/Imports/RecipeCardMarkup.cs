using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using RecipeManager.Core.Modules.Imports;

namespace RecipeManager.Infrastructure.Imports;

// Enrich structured recipe data only when a single, explicitly named recipe card agrees with it.
internal static class RecipeCardMarkup
{
    public static ImportedRecipe Enrich(IDocument document, ImportedRecipe recipe)
    {
        if (recipe.Title is null) return recipe;
        var cards = document.QuerySelectorAll(".wprm-recipe-container")
            .Where(card => string.Equals(Clean(card.QuerySelector(".wprm-recipe-name")?.TextContent), Clean(recipe.Title), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (cards.Length != 1) return recipe;
        var card = cards[0];
        var rows = card.QuerySelectorAll(".wprm-recipe-ingredient");
        var ingredients = recipe.Ingredients;
        if (rows.Length == ingredients.Count && rows.Select((row, i) => string.Equals(Clean(row.QuerySelector(".wprm-recipe-ingredient-name")?.TextContent), Clean(ingredients[i].IngredientName), StringComparison.OrdinalIgnoreCase)).All(matches => matches))
        {
            ingredients = rows.Select((row, i) =>
            {
                var original = recipe.Ingredients[i];
                var notes = Clean(row.QuerySelector(".wprm-recipe-ingredient-notes")?.TextContent);
                if (notes.StartsWith('(') && notes.EndsWith(')')) notes = notes[1..^1].Trim();
                notes = notes.TrimStart(' ', ',');
                var alternate = original.Preparation?.IndexOf("Alternative measure:", StringComparison.Ordinal) ?? -1;
                if (alternate >= 0) notes = Join(notes, original.Preparation![alternate..]);
                var heading = Clean(row.Closest(".wprm-recipe-ingredient-group")?.QuerySelector(".wprm-recipe-ingredient-group-name")?.TextContent).TrimEnd(':').Trim();
                return original with
                {
                    IngredientName = Clean(row.QuerySelector(".wprm-recipe-ingredient-name")!.TextContent),
                    Preparation = string.IsNullOrWhiteSpace(notes) ? null : notes,
                    Group = string.IsNullOrWhiteSpace(heading) ? original.Group : heading
                };
            }).ToArray();
        }
        var recipeNotes = card.QuerySelector(".wprm-recipe-notes");
        return recipe with { Ingredients = ingredients, Notes = recipe.Notes ?? (recipeNotes is null ? null : ReadNotes(recipeNotes)) };
    }
    private static string Clean(string? value) => Regex.Replace(value ?? "", @"\s+", " ", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim();
    private static string Join(string first, string second) => first.Length == 0 ? second : first + "; " + second;
    private static string? ReadNotes(IElement element)
    {
        var text = new StringBuilder();
        void Visit(INode node)
        {
            if (node is IText value) { text.Append(value.Data); return; }
            if (node is not IElement child || child.LocalName is "script" or "style") return;
            var block = child.LocalName is "p" or "div" or "li" or "br" or "h3" or "h4"
                || child.GetAttribute("style")?.Replace(" ", "", StringComparison.Ordinal).Contains("display:block", StringComparison.OrdinalIgnoreCase) == true;
            if (block) text.Append('\n');
            foreach (var item in child.ChildNodes) Visit(item);
            if (block) text.Append('\n');
        }
        Visit(element);
        var notes = string.Join('\n', text.ToString().Split('\n').Select(Clean).Where(line => line.Length > 0));
        if (notes.Length > 20000) throw new ImportFailureException("import_limit");
        return notes.Length == 0 ? null : notes;
    }
}
