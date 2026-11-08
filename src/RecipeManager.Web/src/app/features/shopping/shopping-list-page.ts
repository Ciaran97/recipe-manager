import { Component, DestroyRef, HostListener, effect, inject, untracked } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PortionInput } from '../planner/portion-input';
import { HouseholdSync } from '../../core/sync/household-sync';
import { ShoppingListState } from './shopping-list-state';
import { shoppingUnits, type ShoppingItem, type ShoppingChoice } from './shopping-types';

@Component({
  selector: 'app-shopping-list-page',
  imports: [ReactiveFormsModule, RouterLink, PortionInput, NgTemplateOutlet],
  providers: [ShoppingListState],
  templateUrl: './shopping-list-page.html',
  styleUrl: './shopping-list-page.css',
})
export class ShoppingListPage {
  readonly state = inject(ShoppingListState);
  readonly units = shoppingUnits;
  private readonly destroyed = inject(DestroyRef);
  private previousFocus: HTMLElement | null = null;
  constructor() {
    const sync = inject(HouseholdSync);
    effect((onCleanup) => {
      const id = this.state.current()?.id;
      if (id) onCleanup(sync.watchList(id));
    });
    effect(() => {
      const change = sync.change();
      if (!change) return;
      untracked(() => {
        if (change.list)
          void this.state.remoteChange(change.list.id, change.list.revision, change.list.deleted);
        if (
          change.domains.some(
            (domain) =>
              domain === 'recipesRevision' ||
              domain === 'plannerRevision' ||
              domain === 'settingsRevision',
          )
        )
          this.state.markSourcesChanged();
        if (change.domains.includes('shoppingRevision')) void this.state.recoverRemote();
      });
    });
    const params = inject(ActivatedRoute).snapshot.queryParamMap;
    void this.state
      .initialize()
      .then(() =>
        this.state.receiveIncoming(
          params.get('recipe'),
          params.get('from'),
          params.get('to'),
          params.get('servings'),
        ),
      );
    void this.state.loadCategories();
  }
  hasUnsavedChanges(): boolean {
    return this.state.hasUnsavedChanges();
  }
  isBusy(): boolean {
    return this.state.busy();
  }
  start(action: () => void): void {
    this.previousFocus =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    action();
    setTimeout(() => {
      if (!this.destroyed.destroyed) document.getElementById('shopping-editor-heading')?.focus();
    });
  }
  create(): void {
    this.start(() => this.state.beginCreate());
  }
  manual(): void {
    this.start(() => this.state.beginManual());
  }
  edit(item: ShoppingItem): void {
    this.start(() => this.state.beginEdit(item));
  }
  reconcile(): void {
    this.start(() => this.state.beginReconcile());
  }
  rename(): void {
    this.start(() => this.state.beginRename());
  }
  cancel(): void {
    this.state.cancelEditor();
    if (!this.state.editor()) this.previousFocus?.focus();
  }
  sourceKind(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    if (value === 'mealRange' || value === 'recipes' || value === 'manual')
      this.state.changeKind(value);
  }
  async purchased(item: ShoppingItem, event: Event): Promise<void> {
    const checkbox = event.target as HTMLInputElement;
    const value = checkbox.checked;
    checkbox.checked = item.purchased;
    await this.state.purchase(item, value);
  }
  choice(key: string, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    if (value === 'acceptNew' || value === 'keepUser' || value === 'delete')
      this.state.choose(key, value);
  }
  purchaseChoice(key: string, event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.state.choosePurchased(key, value === '' ? null : value === 'true');
  }
  choiceLabel(value: ShoppingChoice['choice']): string {
    return value === 'acceptNew'
      ? 'Accept updated requirement'
      : value === 'keepUser'
        ? 'Keep my current item'
        : 'Remove requirement';
  }
  alreadyAdded(id: string): boolean {
    return this.state.recipeEntries().some((row) => row.recipeId === id);
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }
}
