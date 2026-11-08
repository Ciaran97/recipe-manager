import type { components } from '../../core/api/generated/schema';
export type Meal = components['schemas']['ScheduledMealRead'];
export type Slot = components['schemas']['MealSlotRead'];
export type Calendar = components['schemas']['MealCalendarRead'];
export type Availability = components['schemas']['LeftoversAvailability'];
export type Recipe = components['schemas']['RecipeRead'];
export type SourceIntent = Omit<
  components['schemas']['SourceResolutionWrite'],
  'expectedSources' | 'dependents'
>;
export const portionPattern = /^(?:\d+(?:\.\d{1,6})?|\.\d{1,6})$/;
export function inputValue(event: Event): string {
  return (event.target as HTMLInputElement).value;
}
