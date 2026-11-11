import { DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterOutlet } from '@angular/router';
import { BehaviorSubject, Observable, Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { FieldsService } from './generated/api/fields.service';
import { ListPlantingHistoryRequestParams, PlantingsService } from './generated/api/plantings.service';

type LoadState<T> =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; data: T };

function load<T>(request: Observable<T>): Observable<LoadState<T>> {
  return request.pipe(
    map((data): LoadState<T> => ({ status: 'ready', data })),
    catchError(() => of<LoadState<T>>({ status: 'error' })),
    startWith<LoadState<T>>({ status: 'loading' }),
  );
}

@Component({
  selector: 'app-planting-history-page',
  imports: [DecimalPipe, RouterLink, RouterOutlet],
  styles: `
    .history-filters { display: flex; flex-wrap: wrap; align-items: end; gap: 1rem; }
    .history-filters .field-control { flex: 1 1 12rem; min-width: 0; }
    .history-table tbody th { width: auto; }
  `,
  template: `
    @if (!showingDetail()) {
      <header class="page-heading"><h1>栽培歷史</h1></header>

      @let fields = fieldState();
      @let crops = cropState();
      @let results = resultState();
      <form class="panel field-form" (submit)="submit($event)">
        <div class="history-filters">
          <div class="field-control">
            <label for="history-field">田區</label>
            <select id="history-field" #field (change)="selectField(field.value)"
              [disabled]="fields.status !== 'ready'">
              <option value="" [selected]="!fieldId()">全部田區</option>
              @if (fields.status === 'ready') {
                @for (option of fields.data; track option.id) {
                  <option [value]="option.id" [selected]="option.id === fieldId()">{{ option.name }}</option>
                }
              }
            </select>
          </div>
          <div class="field-control">
            <label for="history-crop">作物</label>
            <select id="history-crop" #crop (change)="cropName.set(crop.value)"
              [disabled]="crops.status !== 'ready'">
              <option value="" [selected]="!cropName()">全部作物</option>
              @if (crops.status === 'ready') {
                @for (option of crops.data; track option) {
                  <option [value]="option" [selected]="option === cropName()">{{ option }}</option>
                }
              }
            </select>
          </div>
          <button type="submit" class="button"
            [disabled]="fields.status !== 'ready' || results?.status === 'loading'">查詢</button>
        </div>
        @if (fields.status === 'loading' || crops.status === 'loading') {
          <p class="field-form-message" role="status">正在載入篩選選項…</p>
        }
        @if (fields.status === 'error') {
          <p class="field-form-message error" role="alert">無法載入田區選項。</p>
          <button class="button button-outline" type="button" (click)="reloadFields()">重新載入田區</button>
        }
        @if (crops.status === 'error') {
          <p class="field-form-message error" role="alert">無法載入作物選項。</p>
          <button class="button button-outline" type="button" (click)="reloadCrops()">重新載入作物</button>
        }
      </form>

      @switch (results?.status) {
        @case ('loading') {
          <p class="panel empty-state" role="status">正在查詢栽培歷史…</p>
        }
        @case ('error') {
          <section class="panel empty-state">
            <p class="error" role="alert">無法查詢栽培歷史，請再試一次。</p>
            <button class="button button-outline" type="button" (click)="retryQuery()">重試查詢</button>
          </section>
        }
        @case ('ready') {
          @if (results.data.length === 0) {
            <p class="panel empty-state" role="status">查無符合條件的栽培紀錄。</p>
          } @else {
            <div class="panel field-table history-table">
              <table aria-label="栽培歷史紀錄">
                <thead>
                  <tr>
                    <th scope="col">田區</th>
                    <th scope="col">作物</th>
                    <th scope="col">品種</th>
                    <th scope="col">實際栽種日</th>
                    <th scope="col">占用面積</th>
                    <th scope="col">操作</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of results.data; track row.id) {
                    <tr>
                      <td>{{ row.fieldName }}</td>
                      <th scope="row">{{ row.cropName }}</th>
                      <td>{{ row.variety || '—' }}</td>
                      <td>{{ row.actualPlantingDate }}</td>
                      <td>{{ row.areaSquareMeters | number: '1.0-4' }} m²</td>
                      <td>
                        <a [routerLink]="[row.fieldId, row.id]"
                          [attr.aria-label]="'查看詳情：' + row.fieldName + ' ' + row.cropName + ' ' + row.actualPlantingDate">
                          查看詳情
                        </a>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
      }
    }
    <router-outlet (activate)="showingDetail.set(true)" (deactivate)="showingDetail.set(false)" />
  `,
})
export class PlantingHistoryPage {
  private readonly fields = inject(FieldsService);
  private readonly plantings = inject(PlantingsService);
  private readonly fieldRequests = new BehaviorSubject<void>(undefined);
  private readonly cropRequests = new BehaviorSubject<string>('');
  private readonly queries = new Subject<ListPlantingHistoryRequestParams>();
  private lastQuery?: ListPlantingHistoryRequestParams;

  protected readonly fieldId = signal('');
  protected readonly cropName = signal('');
  protected readonly showingDetail = signal(false);
  protected readonly fieldState = toSignal(this.fieldRequests.pipe(
    switchMap(() => load(this.fields.getFields().pipe(map(response => response.fields)))),
  ), { requireSync: true });
  protected readonly cropState = toSignal(this.cropRequests.pipe(
    switchMap(fieldId => load(this.plantings.listPlantingHistoryCrops({ fieldId: fieldId || undefined }))),
  ), { requireSync: true });
  protected readonly resultState = toSignal(this.queries.pipe(
    switchMap(query => load(this.plantings.listPlantingHistory(query))),
  ));

  protected reloadFields(): void {
    this.fieldRequests.next();
  }

  protected reloadCrops(): void {
    this.cropRequests.next(this.fieldId());
  }

  protected selectField(fieldId: string): void {
    this.fieldId.set(fieldId);
    this.cropName.set('');
    this.reloadCrops();
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.fieldState().status !== 'ready' || this.resultState()?.status === 'loading') return;
    this.lastQuery = { fieldId: this.fieldId() || undefined, cropName: this.cropName() || undefined };
    this.queries.next(this.lastQuery);
  }

  protected retryQuery(): void {
    if (this.lastQuery) this.queries.next(this.lastQuery);
  }
}
