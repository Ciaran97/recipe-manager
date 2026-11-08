import type { components } from '../../core/api/generated/schema';

export type ShoppingList = components['schemas']['ShoppingListRead'];
export type ShoppingItem = components['schemas']['ShoppingItemRead'];
export type ShoppingPreview = components['schemas']['ShoppingPreview'];
export type ShoppingChoice = components['schemas']['ShoppingReconciliationChoice'];
export type ShoppingSelection = components['schemas']['ShoppingSelection'];
export type ShoppingBasis = components['schemas']['ShoppingBasis'];
export type QuantityKind = components['schemas']['QuantityKind'];
export type IngredientUnit = components['schemas']['IngredientUnit'];
export type MeasurementStandard = components['schemas']['MeasurementStandard'];
export interface RecipeEntry {
  recipeId: string;
  title: string;
  servings: string;
}
export const shoppingUnits: { value: IngredientUnit; label: string }[] = [
  { value: 'g', label: 'Grams' },
  { value: 'kg', label: 'Kilograms' },
  { value: 'ml', label: 'Millilitres' },
  { value: 'l', label: 'Litres' },
  { value: 'tsp', label: 'Teaspoons' },
  { value: 'tbsp', label: 'Tablespoons' },
  { value: 'cup', label: 'Cups' },
  { value: 'item', label: 'Whole items' },
  { value: 'pinch', label: 'Pinch' },
  { value: 'dash', label: 'Dash' },
  { value: 'handful', label: 'Handful' },
  { value: 'bunch', label: 'Bunch' },
  { value: 'clove', label: 'Clove' },
  { value: 'slice', label: 'Slice' },
];

/** Version comparison only; quantities and consolidation are calculated by the backend. */
export function newerRevision(candidate: string, current: string): boolean {
  return (
    /^[0-9]+$/.test(candidate) && /^[0-9]+$/.test(current) && BigInt(candidate) > BigInt(current)
  );
}
