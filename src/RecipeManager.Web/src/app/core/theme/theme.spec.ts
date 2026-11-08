import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { Theme } from './theme';

describe('Appearance preference', () => {
  let stored: Map<string, string>;
  let originalMedia: PropertyDescriptor | undefined;
  let mediaChanged: ((event: { matches: boolean }) => void) | undefined;
  beforeEach(() => {
    stored = new Map();
    vi.spyOn(window, 'localStorage', 'get').mockReturnValue({
      getItem: (key: string) => stored.get(key) ?? null,
      setItem: (key: string, value: string) => {
        stored.set(key, value);
      },
    } as Storage);
    originalMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn(
        () =>
          ({
            matches: false,
            addEventListener: (_name: string, callback: unknown) => {
              mediaChanged = callback as typeof mediaChanged;
            },
            removeEventListener: vi.fn(),
          }) as unknown as MediaQueryList,
      ),
    });
    TestBed.configureTestingModule({});
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    if (originalMedia) Object.defineProperty(window, 'matchMedia', originalMedia);
    else Reflect.deleteProperty(window, 'matchMedia');
    document.documentElement.removeAttribute('data-theme');
    document.documentElement.style.removeProperty('color-scheme');
  });
  it('follows OS changes only while System is selected', () => {
    const theme = TestBed.inject(Theme);
    expect(theme.preference()).toBe('system');
    expect(theme.resolved()).toBe('light');
    mediaChanged?.({ matches: true });
    expect(theme.resolved()).toBe('dark');
    theme.set('light');
    mediaChanged?.({ matches: true });
    expect(theme.resolved()).toBe('light');
    expect(document.documentElement.dataset['theme']).toBe('light');
    expect(stored.get('recipe-manager.theme')).toBe('light');
  });
  it('restores an explicit preference and synchronizes changes from another tab', () => {
    stored.set('recipe-manager.theme', 'dark');
    const theme = TestBed.inject(Theme);
    expect(theme.resolved()).toBe('dark');
    stored.set('recipe-manager.theme', 'light');
    window.dispatchEvent(new StorageEvent('storage', { key: 'recipe-manager.theme' }));
    expect(theme.preference()).toBe('light');
    expect(document.documentElement.style.colorScheme).toBe('light');
  });
  it('remains usable when preference storage is unavailable', () => {
    vi.spyOn(window, 'localStorage', 'get').mockImplementation(() => {
      throw new Error('Storage disabled');
    });
    const theme = TestBed.inject(Theme);
    expect(theme.preference()).toBe('system');
    theme.set('dark');
    expect(theme.resolved()).toBe('dark');
  });
});
