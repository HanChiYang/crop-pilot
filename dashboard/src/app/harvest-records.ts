import { DecimalPipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Observable, catchError, firstValueFrom, map, of, startWith, switchMap } from 'rxjs';
import { PlantingsService } from './generated/api/plantings.service';
import { HarvestRsDto } from './generated/model/harvestRsDto';
import { HarvestListRsDto } from './generated/model/harvestListRsDto';
import { PlantingRsDto } from './generated/model/plantingRsDto';
import { SaveHarvestRqDto } from './generated/model/saveHarvestRqDto';
import { OperationSort, nextOperationSort, operationError, parseOperationDecimal, taipeiToday } from './operation-form';

type SortColumn = 'harvestDate' | 'weightKg';
type ListState =
  | { status: 'loading' | 'error' }
  | { status: 'ready'; data: HarvestListRsDto };

interface HarvestInput {
  id?: string;
  date: string;
  weight: string;
  description: string;
}

@Component({
  selector: 'app-harvest-records',
  imports: [DecimalPipe, FormsModule],
  templateUrl: './harvest-records.html',
  styleUrl: './harvest-records.css',
})
export class HarvestRecords {
  private readonly plantings = inject(PlantingsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly revision = signal(0);
  private readonly filters = signal<{ startDate?: string; endDate?: string }>({});

  readonly planting = input.required<PlantingRsDto>();

  protected readonly canSave = computed(() => this.planting().status !== 'planned');
  protected readonly columns = [
    { key: 'harvestDate', label: '採收日期' },
    { key: 'weightKg', label: '採收重量' },
  ] as const;
  protected readonly sort = signal<OperationSort<SortColumn>>({ column: 'harvestDate', direction: 'descending' });
  protected readonly startDate = signal('');
  protected readonly endDate = signal('');
  protected readonly invalidDates = computed(() =>
    !!this.startDate() && !!this.endDate() && this.startDate() > this.endDate());
  protected readonly hasFilter = computed(() => !!this.filters().startDate || !!this.filters().endDate);
  protected readonly today = signal(taipeiToday());
  protected readonly editor = signal<HarvestInput | null>(null);
  protected readonly pendingDelete = signal<HarvestRsDto | null>(null);
  protected readonly busy = signal(false);
  protected readonly writeError = signal('');
  protected readonly notice = signal('');

  private readonly query = computed(() => {
    const { column, direction } = this.sort();
    return {
      revision: this.revision(),
      parameters: {
        plantingId: this.planting().id,
        ...this.filters(),
        sortBy: column,
        sortDirection: direction === 'ascending' ? 'asc' as const : 'desc' as const,
      },
    };
  });

  protected readonly state = toSignal(toObservable(this.query).pipe(
    switchMap(({ parameters }) => this.plantings.getHarvets(parameters).pipe(
      map((data): ListState => ({ status: 'ready', data })),
      catchError(() => of<ListState>({ status: 'error' })),
      startWith<ListState>({ status: 'loading' }),
    )),
  ), { initialValue: { status: 'loading' } as ListState });

  protected applyDates(event: SubmitEvent): void {
    event.preventDefault();
    if (this.invalidDates()) return;
    this.filters.set({ startDate: this.startDate() || undefined, endDate: this.endDate() || undefined });
  }

  protected clearDates(): void {
    this.startDate.set('');
    this.endDate.set('');
    this.filters.set({});
  }

  protected sortBy(column: SortColumn): void {
    this.sort.update(current => nextOperationSort(current, column));
  }

  protected reload(): void {
    this.revision.update(value => value + 1);
  }

  protected refreshToday(): void {
    this.today.set(taipeiToday());
  }

  protected openEditor(dialog: HTMLDialogElement, record?: HarvestRsDto): void {
    this.refreshToday();
    this.notice.set('');
    this.writeError.set('');
    this.editor.set({
      id: record?.id,
      date: record?.harvestDate ?? this.today(),
      weight: record?.weightKg.toString() ?? '',
      description: record?.description ?? '',
    });
    dialog.showModal();
  }

  protected async save(input: HarvestInput, dialog: HTMLDialogElement): Promise<void> {
    if (this.busy()) return;
    this.refreshToday();
    if (!input.date || input.date > this.today()) {
      this.writeError.set('請填寫實際採收日期，最晚為今天。');
      return;
    }
    const weightKg = parseOperationDecimal(input.weight);
    if (weightKg === null || weightKg <= 0) {
      this.writeError.set('採收重量須大於零、小於 10 億公斤，且最多六位小數。');
      return;
    }
    if ([...input.description].length > 500 || input.description.includes('\0')) {
      this.writeError.set('採收備註最多 500 字，且不可包含空字元。');
      return;
    }
    const saveHarvestRqDto: SaveHarvestRqDto = {
      harvestDate: input.date,
      weightKg,
      description: input.description.trim() || null,
    };
    const request = input.id
      ? this.plantings.editHarvest({ harvestId: input.id, saveHarvestRqDto })
      : this.plantings.createHarvest({ plantingId: this.planting().id, saveHarvestRqDto });
    await this.write(request, dialog, input.id ? '已更新採收紀錄' : '已新增採收紀錄', '無法儲存');
  }

  protected confirmDelete(record: HarvestRsDto, dialog: HTMLDialogElement): void {
    this.notice.set('');
    this.writeError.set('');
    this.pendingDelete.set(record);
    dialog.showModal();
  }

  protected async remove(record: HarvestRsDto, dialog: HTMLDialogElement): Promise<void> {
    if (this.busy()) return;
    await this.write(this.plantings.deleteHarvest({ harvestId: record.id }), dialog, '已刪除採收紀錄', '無法刪除');
  }

  private async write(request: Observable<unknown>, dialog: HTMLDialogElement, success: string, failure: string) {
    this.busy.set(true);
    this.writeError.set('');
    try {
      await firstValueFrom(request.pipe(takeUntilDestroyed(this.destroyRef)));
      dialog.close();
      this.notice.set(success);
      this.reload();
    } catch (error) {
      if (!this.destroyRef.destroyed) {
        this.writeError.set(operationError(error, failure + '採收紀錄，請再試一次。'));
      }
    } finally {
      this.busy.set(false);
    }
  }
}
