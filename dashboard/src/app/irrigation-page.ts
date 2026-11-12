import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom, forkJoin, Observable } from 'rxjs';
import { PlantingsService } from './generated/api/plantings.service';
import { IrrigationMethod } from './generated/model/irrigationMethod';
import { IrrigationRsDto } from './generated/model/irrigationRsDto';
import { PlantingRsDto } from './generated/model/plantingRsDto';
import { SaveIrrigationRqDto } from './generated/model/saveIrrigationRqDto';
import { operationError, parseOperationDecimal, taipeiToday } from './operation-form';

const methodLabels = {
  drip: '滴灌',
  sprinkler: '噴灌',
  furrow: '溝灌',
  manual: '人工澆灌',
  other: '其他',
} satisfies Record<IrrigationMethod, string>;

interface IrrigationInput {
  id?: string;
  operationDate: string;
  method: IrrigationMethod | '';
  volume: string;
  description: string;
}

@Component({
  selector: 'app-irrigation-page',
  imports: [DecimalPipe, FormsModule, RouterLink],
  styles: `
    .actions { display: flex; flex-wrap: wrap; gap: .65rem; }
    .wide { grid-column: 1 / -1; }
    .field-table .description { white-space: pre-wrap; overflow-wrap: anywhere; min-width: 8rem; }
  `,
  template: `
    <header class="page-heading">
      <div><h1>灌溉紀錄</h1>@if (planting(); as item) { <p>{{ item.cropName }}</p> }</div>
      <div class="actions">
        <a class="button button-outline" [routerLink]="backLink()" [queryParams]="backQuery()">
          返回{{ fromHistory ? '栽培詳情' : '作物栽培管理' }}
        </a>
        @if (!loading() && !loadError() && canSave() && !editor()) {
          <button class="button" type="button" [disabled]="busy()" (click)="open()">新增灌溉</button>
        }
      </div>
    </header>
    @if (notice()) {
      <p class="field-success" role="status">{{ notice() }}</p>
    }
    @if (loading()) {
      <p class="panel empty-state" role="status">正在載入灌溉紀錄…</p>
    } @else if (loadError()) {
      <section class="panel empty-state">
        <p class="error" role="alert">{{ loadError() }}</p>
        <button class="button button-outline" type="button" (click)="load()">重新載入</button>
      </section>
    } @else if (editor(); as input) {
      <form class="panel field-form" (ngSubmit)="save(input)">
        <h2>{{ input.id ? '編輯灌溉' : '新增灌溉' }}</h2>
        <fieldset [disabled]="busy()">
          <div class="field-form-grid">
            <div class="field-control">
              <label for="irrigation-date">灌溉日期</label>
              <input id="irrigation-date" type="date" name="operationDate" required [max]="today()"
                [(ngModel)]="input.operationDate" (focus)="refreshToday()">
            </div>
            <div class="field-control">
              <label for="irrigation-method">灌溉方式</label>
              <select id="irrigation-method" name="method" required [(ngModel)]="input.method">
                <option value="">請選擇灌溉方式</option>
                @for (method of methods; track method) {
                  <option [value]="method">{{ labels[method] }}</option>
                }
              </select>
            </div>
            <div class="field-control">
              <label for="irrigation-volume">用水量（公升，選填）</label>
              <input id="irrigation-volume" name="volume" inputmode="decimal" [(ngModel)]="input.volume">
            </div>
            <div class="field-control wide">
              <label for="irrigation-description">備註（選填）</label>
              <textarea id="irrigation-description" name="description" rows="3"
                [(ngModel)]="input.description"></textarea>
            </div>
          </div>
          @if (message()) {
            <p class="error field-form-message" role="alert">{{ message() }}</p>
          }
          <div class="field-form-actions">
            <button class="button" type="submit">{{ busy() ? '儲存中…' : '儲存' }}</button>
            <button class="button button-outline" type="button" (click)="editor.set(null)">取消</button>
          </div>
        </fieldset>
      </form>
    } @else if (!operations().length) {
      <p class="panel empty-state">{{ canSave() ? '目前沒有灌溉紀錄。' : '開始栽培後即可新增灌溉紀錄。' }}</p>
    } @else {
      <div class="panel field-table">
        <table aria-label="灌溉紀錄清單">
          <thead><tr>
            <th scope="col">灌溉日期</th><th scope="col">灌溉方式</th>
            <th scope="col">用水量</th><th scope="col">備註</th><th scope="col">操作</th>
          </tr></thead>
          <tbody>
            @for (operation of operations(); track operation.id) {
              <tr>
                <td>{{ operation.operationDate }}</td>
                <td>{{ labels[operation.method] }}</td>
                <td>
                  @if (operation.waterVolumeLiters === null) { 未記錄 }
                  @else { {{ operation.waterVolumeLiters | number:'1.0-6' }} 公升 }
                </td>
                <td class="description">{{ operation.description || '—' }}</td>
                <td>
                  <div class="actions">
                    @if (canSave()) {
                      <button class="button button-outline" type="button" [disabled]="busy()"
                        (click)="open(operation)">編輯</button>
                    }
                    <button class="button button-outline" type="button" [disabled]="busy()"
                      (click)="confirmDelete(operation, deleteDialog)">刪除</button>
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
    <dialog #deleteDialog class="field-delete-dialog" aria-labelledby="irrigation-delete-title"
      (cancel)="busy() && $event.preventDefault()">
      <h2 id="irrigation-delete-title">刪除灌溉紀錄</h2>
      @if (pendingDelete(); as operation) {
        <p>確定刪除 {{ operation.operationDate }} 的{{ labels[operation.method] }}紀錄？</p>
        @if (deleteError()) {
          <p class="error" role="alert">{{ deleteError() }}</p>
        }
        <div class="field-form-actions">
          <button class="button button-outline" type="button" autofocus [disabled]="busy()"
            (click)="deleteDialog.close()">取消</button>
          <button class="button" type="button" [disabled]="busy()" (click)="remove(operation, deleteDialog)">
            {{ busy() ? '刪除中…' : '確認刪除' }}
          </button>
        </div>
      }
    </dialog>
  `,
})
export class IrrigationPage {
  private readonly route = inject(ActivatedRoute);
  private readonly plantings = inject(PlantingsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ids = signal({ fieldId: '', plantingId: '' });

  protected readonly fromHistory = this.route.snapshot.data['fromHistory'] === true;
  protected readonly backLink = computed(() => this.fromHistory
    ? ['/dashboard/planting-history', this.ids().fieldId, this.ids().plantingId]
    : ['/dashboard/plantings']);
  protected readonly backQuery = computed(() => this.fromHistory ? {} : { fieldId: this.ids().fieldId });
  protected readonly labels = methodLabels;
  protected readonly methods = Object.values(IrrigationMethod);
  protected readonly today = signal(taipeiToday());
  protected readonly planting = signal<PlantingRsDto | null>(null);
  protected readonly canSave = computed(() => this.planting() !== null && this.planting()?.status !== 'planned');
  protected readonly operations = signal<IrrigationRsDto[]>([]);
  protected readonly editor = signal<IrrigationInput | null>(null);
  protected readonly pendingDelete = signal<IrrigationRsDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly loadError = signal('');
  protected readonly message = signal('');
  protected readonly deleteError = signal('');
  protected readonly notice = signal('');

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      this.ids.set({ fieldId: params.get('fieldId')!, plantingId: params.get('plantingId')! });
      this.editor.set(null);
      this.planting.set(null);
      this.notice.set('');
      void this.load();
    });
  }

  protected async load(): Promise<void> {
    const ids = this.ids();
    this.loading.set(true);
    this.loadError.set('');
    try {
      const data = await firstValueFrom(forkJoin({
        planting: this.plantings.getPlanting(ids),
        operations: this.plantings.listIrrigations({ plantingId: ids.plantingId }),
      }).pipe(takeUntilDestroyed(this.destroyRef)));
      if (ids !== this.ids()) return;
      if (this.fromHistory && data.planting.status !== 'completed') {
        this.loadError.set('找不到這筆已結束的栽培紀錄。');
        return;
      }
      this.planting.set(data.planting);
      this.operations.set(data.operations);
    } catch (error) {
      if (ids === this.ids()) {
        this.loadError.set(error instanceof HttpErrorResponse && error.status === 404
          ? '找不到這筆栽培紀錄。'
          : '無法載入灌溉紀錄，請再試一次。');
      }
    } finally {
      if (ids === this.ids()) this.loading.set(false);
    }
  }

  protected open(operation?: IrrigationRsDto): void {
    this.refreshToday();
    this.notice.set('');
    this.message.set('');
    this.editor.set({
      id: operation?.id,
      operationDate: operation?.operationDate ?? this.today(),
      method: operation?.method ?? '',
      volume: operation?.waterVolumeLiters?.toFixed(6).replace(/\.?0+$/, '') ?? '',
      description: operation?.description ?? '',
    });
  }

  protected async save(input: IrrigationInput): Promise<void> {
    if (this.busy()) return;
    this.refreshToday();
    if (!input.operationDate || input.operationDate > this.today() || !input.method) {
      this.message.set('請確認灌溉日期與灌溉方式，日期最晚為今天。');
      return;
    }
    const volume = input.volume.trim();
    const waterVolumeLiters = volume ? parseOperationDecimal(volume) : null;
    if (volume && (waterVolumeLiters === null || waterVolumeLiters <= 0)) {
      this.message.set('用水量須大於零、小於 10 億，且最多六位小數。');
      return;
    }
    const description = input.description.trim();
    if ([...description].length > 500 || description.includes('\0')) {
      this.message.set('灌溉備註最多 500 字，且不可包含空字元。');
      return;
    }
    const saveIrrigationRqDto: SaveIrrigationRqDto = {
      operationDate: input.operationDate,
      method: input.method,
      waterVolumeLiters,
      description: description || null,
    };
    this.message.set('');
    const request = input.id
      ? this.plantings.editIrrigation({ irrigationId: input.id, saveIrrigationRqDto })
      : this.plantings.createIrrigation({ plantingId: this.ids().plantingId, saveIrrigationRqDto });
    await this.write(request, () => this.editor.set(null),
      input.id ? '已更新灌溉紀錄' : '已新增灌溉紀錄',
      error => this.message.set(operationError(error, '無法儲存灌溉紀錄，請再試一次。')));
  }

  protected refreshToday(): void {
    this.today.set(taipeiToday());
  }

  protected confirmDelete(operation: IrrigationRsDto, dialog: HTMLDialogElement): void {
    this.notice.set('');
    this.pendingDelete.set(operation);
    this.deleteError.set('');
    dialog.showModal();
  }

  protected async remove(operation: IrrigationRsDto, dialog: HTMLDialogElement): Promise<void> {
    if (this.busy()) return;
    this.deleteError.set('');
    await this.write(this.plantings.deleteIrrigation({ irrigationId: operation.id }),
      () => dialog.close(), '已刪除灌溉紀錄',
      error => this.deleteError.set(operationError(error, '無法刪除灌溉紀錄，請再試一次。')));
  }

  private async write(
    request: Observable<unknown>, onSuccess: () => void, notice: string, onError: (error: unknown) => void,
  ): Promise<void> {
    const ids = this.ids();
    this.busy.set(true);
    try {
      await firstValueFrom(request.pipe(takeUntilDestroyed(this.destroyRef)));
      if (ids !== this.ids()) return;
      onSuccess();
      this.notice.set(notice);
      await this.load();
    } catch (error) {
      if (ids === this.ids()) onError(error);
    } finally {
      this.busy.set(false);
    }
  }
}
