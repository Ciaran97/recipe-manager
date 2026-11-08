import {
  Component,
  HostListener,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { AuthSession } from './features/auth/auth-session';
import { LibraryNavigation } from './core/session/library-navigation';
import { Icon } from './core/ui/icon';
import { Theme } from './core/theme/theme';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, Icon],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly injector = inject(Injector);
  readonly auth = inject(AuthSession);
  readonly library = inject(LibraryNavigation);
  readonly theme = inject(Theme);
  private readonly router = inject(Router);
  readonly navigationOpen = signal(false);
  readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map((event) => {
        this.navigationOpen.set(false);
        return event.urlAfterRedirects;
      }),
    ),
    { initialValue: this.router.url },
  );
  readonly context = computed(() => {
    const path = this.url().split('?')[0];
    if (path.startsWith('/recipes')) return 'Recipe library';
    if (path.startsWith('/meal-planner')) return 'Meal planner';
    if (path.startsWith('/shopping-lists')) return 'Shopping lists';
    if (path.startsWith('/ingredients')) return 'Ingredient catalogue';
    if (path.startsWith('/settings')) return 'Settings';
    return 'Your household kitchen';
  });
  readonly globalQuery = computed(
    () => new URLSearchParams(this.url().split('?')[1]).get('q') ?? '',
  );
  readonly selectedCollection = computed(() =>
    new URLSearchParams(this.url().split('?')[1]).get('collection'),
  );
  readonly favourites = computed(
    () => new URLSearchParams(this.url().split('?')[1]).get('favouriteOnly') === 'true',
  );
  readonly allRecipes = computed(
    () => this.url().startsWith('/recipes') && !this.selectedCollection() && !this.favourites(),
  );
  searchRecipes(value: string): void {
    void this.router.navigate(['/recipes'], {
      queryParams: value.trim() ? { q: value.trim() } : {},
    });
  }
  toggleNavigation(): void {
    if (this.navigationOpen()) {
      this.closeNavigation();
      return;
    }
    this.navigationOpen.set(true);
    afterNextRender(() => document.querySelector<HTMLElement>('.sidebar-close')?.focus(), {
      injector: this.injector,
    });
  }
  @HostListener('window:resize') resized(): void {
    if (window.matchMedia('(min-width: 60.001rem)').matches) this.navigationOpen.set(false);
  }
  @HostListener('document:keydown', ['$event']) containNavigationFocus(event: KeyboardEvent): void {
    if (!this.navigationOpen() || event.key !== 'Tab') return;
    const elements = Array.from(
      document.querySelectorAll<HTMLElement>(
        '#main-sidebar a[href], #main-sidebar button, #main-sidebar input',
      ),
    );
    const first = elements[0],
      last = elements.at(-1);
    if (event.shiftKey && document.activeElement === first) {
      last?.focus();
      event.preventDefault();
    } else if (!event.shiftKey && document.activeElement === last) {
      first?.focus();
      event.preventDefault();
    }
  }
  @HostListener('document:keydown.escape') closeNavigation(): void {
    if (this.navigationOpen()) {
      this.navigationOpen.set(false);
      afterNextRender(() => document.getElementById('navigation-toggle')?.focus(), {
        injector: this.injector,
      });
    }
  }
}
