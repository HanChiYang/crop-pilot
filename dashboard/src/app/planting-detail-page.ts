import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, catchError, forkJoin, map, of, startWith, switchMap, tap } from 'rxjs';
import { CompletionDateDialog } from './completion-date-dialog';
import { FieldsService } from './generated/api/fields.service';
import { PlantingsService } from './generated/api/plantings.service';
import { PlantingRsDto } from './generated/model/plantingRsDto';
import { HarvestRecords } from './harvest-records';
import { formatSlashDate, plantingStatusLabel } from './planting-display';
import { PlantingHistoryPage } from './planting-history-page';

type DetailState =
  | { status: 'loading' | 'error' | 'missing' }
  | { status: 'ready'; fieldName: string; planting: PlantingRsDto };

const detailTabs = [
  { id: 'harvest', label: '採收紀錄' },
  { id: 'sales', label: '販售紀錄' },
  { id: 'costs', label: '成本紀錄' },
  { id: 'profit', label: '累計損益' },
] as const;

type DetailTab = (typeof detailTabs)[number]['id'];

@Component({
  selector: 'app-planting-detail-page',
  imports: [DecimalPipe, RouterLink, HarvestRecords, CompletionDateDialog],
  styles: `
    dl { margin: 0; overflow-wrap: anywhere; }
    dt { font-weight: 700; }
    dd { margin: .3rem 0 0; }
    .notes { grid-column: 1 / -1; white-space: pre-wrap; }
    .operations, .detail-title { display: flex; flex-wrap: wrap; align-items: center; gap: .65rem; }
    .detail-status { padding: .25rem .7rem; border-radius: 999px; background: var(--pale); font-size: .875rem; font-weight: 700; }
    .detail-notice { color: var(--green); font-size: .95rem; font-weight: 700; }
    .detail-summary { max-width: none; }
    .summary-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr)); gap: 1rem; }
    .completion-date-cell { display: flex; flex-wrap: wrap; align-items: center; gap: .5rem; }
    .button-compact { min-height: 1.9rem; padding: .2rem .65rem; font-size: .875rem; }
    .detail-tabs {
      display: flex;
      flex-wrap: wrap;
      gap: .25rem .8rem;
      margin-top: 1.2rem;
      border-bottom: 1px solid var(--border);
    }
    .detail-tabs button {
      padding: .65rem .85rem;
      border: 0;
      border-bottom: 2px solid transparent;
      background: transparent;
      color: var(--muted);
      font: inherit;
      cursor: pointer;
    }
    .detail-tabs button:hover { color: var(--green); }
    .detail-tabs button[aria-selected="true"] {
      border-bottom-color: var(--green);
      color: var(--green);
      font-weight: 700;
    }
  `,
  template: `
    @let detail = state();
    <header class="page-heading">
      <div class="detail-title">
        <h1>{{ detail.status === 'ready' ? detail.planting.cropName : '栽培紀錄詳情' }}</h1>
        @if (detail.status === 'ready') {
          <span class="detail-status">{{ statusLabel[detail.planting.status] }}</span>
          @if (completionNotice(); as notice) {
            <span class="detail-notice" role="status">{{ notice }}</span>
          }
        }
      </div>
      <a class="button button-outline" [routerLink]="operationBase" [queryParams]="backQuery">
        返回{{ fromHistory ? '栽培歷史' : '作物栽培' }}
      </a>
    </header>
    @switch (detail.status) {
      @case ('loading') {
        <p class="panel empty-state" role="status">正在載入栽培紀錄…</p>
      }
      @case ('missing') {
        <p class="panel empty-state" role="status">找不到這筆{{ fromHistory ? '已結束的' : '' }}栽培紀錄。</p>
      }
      @case ('error') {
        <section class="panel empty-state">
          <p class="error" role="alert">無法載入栽培紀錄，請再試一次。</p>
          <button class="button button-outline" type="button" (click)="reload.next()">重新載入</button>
        </section>
      }
      @case ('ready') {
        @let planting = detail.planting;
        <p class="operations">
          <a class="button button-outline" [queryParams]="backQuery"
            [routerLink]="[operationBase, planting.fieldId, planting.id, 'fertilizations']">資材施用紀錄</a>
          <a class="button button-outline" [queryParams]="backQuery"
            [routerLink]="[operationBase, planting.fieldId, planting.id, 'irrigations']">灌溉紀錄</a>
        </p>
        <section class="panel field-form detail-summary" aria-label="栽培紀錄資料">
          <dl class="summary-grid">
            <div><dt>田區</dt><dd>{{ detail.fieldName }}</dd></div>
            <div><dt>品種</dt><dd>{{ planting.variety || '—' }}</dd></div>
            @if (fromHistory || planting.plannedPlantingDate) {
              <div><dt>預定栽種日</dt><dd>{{ planting.plannedPlantingDate || '—' }}</dd></div>
            }
            <div><dt>實際栽種日</dt><dd>{{ planting.actualPlantingDate || '—' }}</dd></div>
            @if (planting.completionDate) {
              <div>
                <dt>結束日</dt>
                <dd class="completion-date-cell">
                  <span>{{ planting.completionDate }}</span>
                  @if (fromHistory && planting.status === 'completed') {
                    <button class="button button-outline button-compact" type="button"
                      (click)="openCompletionDialog()">修正</button>
                  }
                </dd>
              </div>
            }
            <div><dt>占用面積</dt><dd>{{ planting.areaSquareMeters | number: '1.0-4' }} m²</dd></div>
            @if (fromHistory || planting.notes) {
              <div class="notes"><dt>備註</dt><dd>{{ planting.notes || '—' }}</dd></div>
            }
          </dl>
        </section>
        @if (fromHistory && planting.status === 'completed') {
          <app-completion-date-dialog [fieldName]="detail.fieldName" [planting]="planting"
            (saved)="onCompletionDateSaved($event)" />
        }
        <div #tablist class="detail-tabs" role="tablist" aria-label="栽培詳情">
          @for (tab of tabs; track tab.id; let index = $index) {
            <button type="button" role="tab" [id]="'detail-tab-' + tab.id"
              [attr.aria-selected]="selectedTab() === tab.id" [attr.aria-controls]="'detail-panel-' + tab.id"
              [attr.tabindex]="selectedTab() === tab.id ? 0 : -1"
              (click)="selectedTab.set(tab.id)" (keydown)="handleTabKey($event, index, tablist)">
              {{ tab.label }}
            </button>
          }
        </div>

        @for (tab of tabs; track tab.id) {
          <div role="tabpanel" tabindex="0" [id]="'detail-panel-' + tab.id"
            [attr.aria-labelledby]="'detail-tab-' + tab.id" [hidden]="selectedTab() !== tab.id">
            @if (tab.id === 'harvest') {
              <app-harvest-records [planting]="planting" />
            } @else {
              <p class="panel empty-state">{{ tab.label }}開發中。</p>
            }
          </div>
        }
      }
    }
  `,
})
export class PlantingDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly fields = inject(FieldsService);
  private readonly plantings = inject(PlantingsService);
  private readonly historyPage = inject(PlantingHistoryPage, { optional: true });
  private readonly completionDialog = viewChild(CompletionDateDialog);

  protected readonly fromHistory = this.route.snapshot.data['fromHistory'] === true;
  protected readonly operationBase = this.fromHistory ? '/dashboard/planting-history' : '/dashboard/plantings';
  protected readonly backQuery = this.fromHistory ? {} : { fieldId: this.route.snapshot.queryParamMap.get('fieldId') };
  protected readonly statusLabel = plantingStatusLabel;
  protected readonly tabs = detailTabs;
  protected readonly selectedTab = signal<DetailTab>('harvest');
  protected readonly completionNotice = signal('');
  private readonly plantingOverride = signal<PlantingRsDto | null>(null);
  protected readonly reload = new Subject<void>();
  private readonly loadedState = toSignal(this.route.paramMap.pipe(
    switchMap(params => this.reload.pipe(
      startWith(undefined),
      tap(() => {
        this.selectedTab.set('harvest');
        this.plantingOverride.set(null);
        this.completionNotice.set('');
      }),
      switchMap(() => {
        const fieldId = params.get('fieldId')!;
        const plantingId = params.get('plantingId')!;
        return forkJoin({
          field: this.fields.getField({ fieldId }),
          planting: this.plantings.getPlanting({ plantingId }),
        }).pipe(
          map(({ field, planting }): DetailState =>
            planting.fieldId === field.id && (!this.fromHistory || planting.status === 'completed')
              ? { status: 'ready', fieldName: field.name, planting }
              : { status: 'missing' }),
          catchError((error: unknown) => of<DetailState>({
            status: error instanceof HttpErrorResponse && error.status === 404 ? 'missing' : 'error',
          })),
          startWith<DetailState>({ status: 'loading' }),
        );
      }),
    )),
  ), { initialValue: { status: 'loading' } });
  protected readonly state = computed<DetailState>(() => {
    const loaded = this.loadedState();
    const override = this.plantingOverride();
    if (loaded.status === 'ready' && override && override.id === loaded.planting.id) {
      return { status: 'ready', fieldName: loaded.fieldName, planting: override };
    }
    return loaded;
  });

  protected openCompletionDialog(): void {
    this.completionDialog()?.open();
  }

  protected onCompletionDateSaved(updated: PlantingRsDto): void {
    this.plantingOverride.set(updated);
    if (updated.completionDate) {
      this.completionNotice.set(`結束日已更新為 ${formatSlashDate(updated.completionDate)}。`);
    }
    this.historyPage?.refreshResults();
  }

  protected handleTabKey(event: KeyboardEvent, index: number, tablist: HTMLElement): void {
    const last = this.tabs.length - 1;
    let next: number;
    switch (event.key) {
      case 'ArrowLeft': next = index === 0 ? last : index - 1; break;
      case 'ArrowRight': next = index === last ? 0 : index + 1; break;
      case 'Home': next = 0; break;
      case 'End': next = last; break;
      default: return;
    }
    event.preventDefault();
    this.selectedTab.set(this.tabs[next].id);
    tablist.querySelectorAll<HTMLButtonElement>('button')[next].focus();
  }
}
