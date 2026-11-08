import { addDays, householdToday, mondayOf, weekDays } from './planner-dates';

describe('Household calendar presentation', () => {
  it('uses the household timezone at a UTC date boundary', () => {
    const now = new Date('2026-11-01T16:15:00Z');
    expect(householdToday('Australia/Perth', now)).toBe('2026-11-02');
    expect(householdToday('UTC', now)).toBe('2026-11-01');
  });
  it('starts on Monday and navigates civil dates without DST or local timezone shifts', () => {
    expect(mondayOf('2026-11-01')).toBe('2026-10-26');
    expect(mondayOf('2026-11-02')).toBe('2026-11-02');
    expect(addDays('2024-02-28', 1)).toBe('2024-02-29');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(weekDays('2026-11-02')).toHaveLength(7);
    expect(weekDays('2026-11-02').at(-1)).toBe('2026-11-08');
  });
});
