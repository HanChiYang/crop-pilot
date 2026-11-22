import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subscription, firstValueFrom } from 'rxjs';
import { PlantingsService } from './generated/api/plantings.service';
import { CompletionDateLowerBoundSource } from './generated/model/completionDateLowerBoundSource';
import { PlantingCompletionDateRangeRsDto } from './generated/model/plantingCompletionDateRangeRsDto';
import { PlantingRsDto } from './generated/model/plantingRsDto';
import { completionDateLowerBoundLabel, formatSlashDate, plantingStatusLabel } from './planting-display';

type RangeLoadState = 'idle' | 'loading' | 'ready' | 'error';

const conflictBySource: Record<CompletionDateLowerBoundSource, (date: string) => string> = {
  harvest: date => `已有 ${date} 的採收紀錄，結束日不得早於這天。`,
  materialApplication: date => `已有 ${date} 的資材施用紀錄，結束日不得早於這天。`,
  irrigation: date => `已有 ${date} 的灌溉紀錄，結束日不得早於這天。`,
  actualPlanting: date => `結束日不得早於 ${date}（實際栽種日）。`,
};

@Component({
  selector: 'app-completion-date-dialog',
  imports: [FormsModule],
  styles: `
    .completion-dialog { width: min(30rem, calc(100% - 2rem)); padding: 1.5rem; }
    .dialog-header { display: flex; align-items: flex-start; justify-content: space-between; gap: 1rem; }
    .dialog-header h2 { margin: 0; font-size: 1.35rem; }
    .dialog-subtitle { margin: .2rem 0 0; font-size: .92rem; color: var(--muted); }
    .dialog-close {
      min-width: 2rem;
      min-height: 2rem;
      padding: .15rem .45rem;
      border: 0;
      border-radius: .4rem;
      background: transparent;
      color: var(--muted);
      font-size: 1.25rem;
      line-height: 1;
      cursor: pointer;
    }
    .dialog-close:hover:not(:disabled) { background: var(--pale); color: var(--green); }
    .dialog-close:disabled { opacity: .5; cursor: default; }
    .current-summary {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: .75rem 1rem;
      margin: 1rem 0;
      padding: .85rem 1rem;
      border-radius: .65rem;
      background: #f6f8f4;
    }
    .current-summary dt { font-size: .85rem; color: var(--muted); font-weight: 500; }
    .current-summary dd { margin: .2rem 0 0; font-weight: 700; color: #172b22; }
    .required-mark { color: #a33030; }
    .field-control input:disabled { background: #f6f8f4; color: var(--muted); cursor: default; }
    .helper-text { margin: .4rem 0 0; font-size: .85rem; color: var(--muted); }
    .error-banner {
      margin-top: .9rem;
      padding: .65rem .85rem;
      border-radius: .5rem;
      background: #fbeeed;
      color: #a33030;
      font-size: .9rem;
    }
    .dialog-actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: .65rem;
      margin-top: 1.25rem;
      padding-top: 1rem;
      border-top: 1px solid var(--border);
    }
  `,
  template: `
    <dialog #dialog class="field-delete-dialog completion-dialog" aria-labelledby="completion-date-title"
      (cancel)="onCancel($event)" (close)="reset()">
      <div class="dialog-header">
        <div>
          <h2 id="completion-date-title">修正結束日</h2>
          <p class="dialog-subtitle">{{ fieldName() }}・{{ planting().cropName }}・{{ statusLabel[planting().status] }}</p>
        </div>
        <button class="dialog-close" type="button" aria-label="關閉" [disabled]="saving()"
          (click)="dialog.close()">×</button>
      </div>

      <dl class="current-summary">
        <div>
          <dt>實際栽種日</dt>
          <dd>{{ planting().actualPlantingDate ? formatDate(planting().actualPlantingDate!) : '—' }}</dd>
        </div>
        <div>
          <dt>原結束日</dt>
          <dd>{{ originalCompletionDate() ? formatDate(originalCompletionDate()) : '—' }}</dd>
        </div>
      </dl>

      <form novalidate (ngSubmit)="save()">
        <div class="field-control">
          <label for="completion-date-input">結束日 <span class="required-mark" aria-hidden="true">*</span></label>
          <input id="completion-date-input" type="date" name="completionDate" required
            [min]="range()?.earliestDate ?? null" [max]="range()?.latestDate ?? null"
            [disabled]="inputDisabled()" [ngModel]="dateInput()" (ngModelChange)="onDateInput($event)"
            [attr.aria-invalid]="fieldError() ? 'true' : null"
            [attr.aria-describedby]="fieldError() ? 'completion-date-error' : null">
        </div>

        @if (fieldError()) {
          <p id="completion-date-error" class="error" role="alert">{{ fieldError() }}</p>
        }

        @if (rangeState() === 'loading') {
          <p class="helper-text" role="status">正在載入可填範圍…</p>
        } @else if (rangeState() === 'ready') {
          @if (range(); as current) {
            <p class="helper-text">
              可填範圍：{{ formatDate(current.earliestDate) }}（{{ lowerBoundLabels[current.earliestDateSource] }}）至
              {{ formatDate(current.latestDate) }}（今天）
            </p>
          }
        }

        <p class="helper-text">修正後，補登的採收與田間作業日期不得晚於新的結束日。</p>

        @if (bannerError(); as banner) {
          <p class="error-banner" role="alert">{{ banner }}</p>
        }

        <div class="dialog-actions">
          <button class="button button-outline" type="button" [disabled]="saving()"
            (click)="dialog.close()">取消</button>
          @if (rangeState() === 'error') {
            <button class="button button-outline" type="button" (click)="loadRange()">重新載入</button>
          }
          <button class="button" type="submit" [disabled]="saveDisabled()">
            {{ saving() ? '儲存中…' : '儲存' }}
          </button>
        </div>
      </form>
    </dialog>
  `,
})
export class CompletionDateDialog {
  private readonly plantings = inject(PlantingsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private rangeSub?: Subscription;

  readonly fieldName = input.required<string>();
  readonly planting = input.required<PlantingRsDto>();
  readonly saved = output<PlantingRsDto>();

  protected readonly statusLabel = plantingStatusLabel;
  protected readonly lowerBoundLabels = completionDateLowerBoundLabel;
  protected readonly formatDate = formatSlashDate;
  protected readonly rangeState = signal<RangeLoadState>('idle');
  protected readonly range = signal<PlantingCompletionDateRangeRsDto | null>(null);
  protected readonly originalCompletionDate = signal('');
  protected readonly dateInput = signal('');
  protected readonly fieldError = signal('');
  protected readonly saveError = signal('');
  protected readonly saving = signal(false);

  protected readonly inputDisabled = computed(() => this.rangeState() !== 'ready' || this.saving());
  protected readonly saveDisabled = computed(() => this.rangeState() !== 'ready' || this.saving());
  protected readonly bannerError = computed(() =>
    this.rangeState() === 'error' ? '無法載入可填範圍，請再試一次。' : this.saveError(),
  );

  constructor() {
    this.destroyRef.onDestroy(() => this.rangeSub?.unsubscribe());
  }

  open(): void {
    const initial = this.planting().completionDate ?? '';
    this.originalCompletionDate.set(initial);
    this.dateInput.set(initial);
    this.fieldError.set('');
    this.saveError.set('');
    this.range.set(null);
    this.saving.set(false);
    const nativeDialog = this.dialog().nativeElement;
    if (!nativeDialog.open) nativeDialog.showModal();
    this.loadRange();
  }

  protected loadRange(): void {
    this.rangeSub?.unsubscribe();
    this.rangeState.set('loading');
    this.saveError.set('');
    this.rangeSub = this.plantings.getPlantingCompletionDateRange({ plantingId: this.planting().id }).subscribe({
      next: result => {
        this.range.set(result);
        this.originalCompletionDate.set(result.completionDate);
        this.dateInput.set(result.completionDate);
        this.rangeState.set('ready');
      },
      error: () => {
        this.range.set(null);
        this.rangeState.set('error');
      },
    });
  }

  protected onDateInput(value: string): void {
    this.dateInput.set(value);
    this.fieldError.set('');
    this.saveError.set('');
  }

  protected onCancel(event: Event): void {
    if (this.saving()) event.preventDefault();
  }

  protected reset(): void {
    this.rangeSub?.unsubscribe();
    this.rangeSub = undefined;
    this.rangeState.set('idle');
    this.range.set(null);
    this.fieldError.set('');
    this.saveError.set('');
    this.saving.set(false);
  }

  protected async save(): Promise<void> {
    if (this.saveDisabled()) return;
    const currentRange = this.range();
    if (!currentRange) return;

    const value = this.dateInput().trim();
    this.fieldError.set('');
    this.saveError.set('');

    if (!value) {
      this.fieldError.set('請填寫結束日。');
      return;
    }
    if (value > currentRange.latestDate) {
      this.fieldError.set('結束日不得晚於今天。');
      return;
    }
    if (value < currentRange.earliestDate) {
      const source = completionDateLowerBoundLabel[currentRange.earliestDateSource];
      this.fieldError.set(`結束日不得早於 ${formatSlashDate(currentRange.earliestDate)}（${source}）。`);
      return;
    }

    this.saving.set(true);
    try {
      const updated = await firstValueFrom(
        this.plantings.editPlantingCompletionDate({
          plantingId: this.planting().id,
          editPlantingCompletionDateRqDto: { completionDate: value },
        }).pipe(takeUntilDestroyed(this.destroyRef)),
      );
      this.dialog().nativeElement.close();
      this.saved.emit(updated);
    } catch (error) {
      if (this.destroyRef.destroyed) return;
      await this.handleSaveError(error);
    } finally {
      if (!this.destroyRef.destroyed) this.saving.set(false);
    }
  }

  private async handleSaveError(error: unknown): Promise<void> {
    const httpError = error instanceof HttpErrorResponse ? error : null;
    const problem = httpError?.error as { errors?: Record<string, string[]> } | null | undefined;
    const serverDateMessage = problem?.errors?.['completionDate']?.[0];

    if (httpError?.status === 409 && serverDateMessage) {
      try {
        const refreshed = await firstValueFrom(
          this.plantings.getPlantingCompletionDateRange({ plantingId: this.planting().id })
            .pipe(takeUntilDestroyed(this.destroyRef)),
        );
        if (this.destroyRef.destroyed || !this.dialog().nativeElement.open) return;
        this.range.set(refreshed);
        this.originalCompletionDate.set(refreshed.completionDate);
        this.fieldError.set(conflictBySource[refreshed.earliestDateSource](formatSlashDate(refreshed.earliestDate)));
      } catch {
        if (!this.destroyRef.destroyed) this.fieldError.set(serverDateMessage);
      }
      return;
    }

    if (httpError?.status === 400 && serverDateMessage) {
      this.fieldError.set(serverDateMessage);
      return;
    }

    this.saveError.set('無法儲存結束日，請再試一次。');
  }
}
