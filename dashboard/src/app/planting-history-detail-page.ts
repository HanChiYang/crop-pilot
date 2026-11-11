import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, catchError, forkJoin, map, of, startWith, switchMap } from 'rxjs';
import { FieldsService } from './generated/api/fields.service';
import { PlantingsService } from './generated/api/plantings.service';
import { PlantingRsDto } from './generated/model/plantingRsDto';
import { plantingStatusLabel } from './planting-display';

type DetailState =
  | { status: 'loading' | 'error' | 'missing' }
  | { status: 'ready'; fieldName: string; planting: PlantingRsDto };

@Component({
  selector: 'app-planting-history-detail-page',
  imports: [DecimalPipe, RouterLink],
  styles: `
    dl { margin: 0; overflow-wrap: anywhere; }
    dt { font-weight: 700; }
    dd { margin: .3rem 0 0; }
    .notes { grid-column: 1 / -1; white-space: pre-wrap; }
  `,
  template: `
    <header class="page-heading">
      <h1>栽培紀錄詳情</h1>
      <a class="button button-outline" routerLink="/dashboard/planting-history">返回栽培歷史</a>
    </header>
    @let detail = state();
    @switch (detail.status) {
      @case ('loading') {
        <p class="panel empty-state" role="status">正在載入栽培紀錄…</p>
      }
      @case ('missing') {
        <p class="panel empty-state" role="status">找不到這筆已結束的栽培紀錄。</p>
      }
      @case ('error') {
        <section class="panel empty-state">
          <p class="error" role="alert">無法載入栽培紀錄，請再試一次。</p>
          <button class="button button-outline" type="button" (click)="reload.next()">重新載入</button>
        </section>
      }
      @case ('ready') {
        @let planting = detail.planting;
        <section class="panel field-form" aria-label="栽培紀錄資料">
          <dl class="field-form-grid">
            <div><dt>田區</dt><dd>{{ detail.fieldName }}</dd></div>
            <div><dt>作物</dt><dd>{{ planting.cropName }}</dd></div>
            <div><dt>品種</dt><dd>{{ planting.variety || '—' }}</dd></div>
            <div><dt>狀態</dt><dd>{{ statusLabel[planting.status] }}</dd></div>
            <div><dt>預定栽種日</dt><dd>{{ planting.plannedPlantingDate || '—' }}</dd></div>
            <div><dt>實際栽種日</dt><dd>{{ planting.actualPlantingDate || '—' }}</dd></div>
            <div><dt>占用面積</dt><dd>{{ planting.areaSquareMeters | number: '1.0-4' }} m²</dd></div>
            <div class="notes"><dt>備註</dt><dd>{{ planting.notes || '—' }}</dd></div>
          </dl>
        </section>
      }
    }
  `,
})
export class PlantingHistoryDetailPage {
  private readonly route = inject(ActivatedRoute);
  private readonly fields = inject(FieldsService);
  private readonly plantings = inject(PlantingsService);

  protected readonly statusLabel = plantingStatusLabel;
  protected readonly reload = new Subject<void>();
  protected readonly state = toSignal(this.route.paramMap.pipe(
    switchMap(params => this.reload.pipe(
      startWith(undefined),
      switchMap(() => {
        const fieldId = params.get('fieldId')!;
        const plantingId = params.get('plantingId')!;
        return forkJoin({
          field: this.fields.getField({ fieldId }),
          planting: this.plantings.getPlanting({ fieldId, plantingId }),
        }).pipe(
          map(({ field, planting }): DetailState => planting.status === 'completed'
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
}
