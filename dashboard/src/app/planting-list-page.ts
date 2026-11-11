import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  catchError,
  combineLatest,
  distinctUntilChanged,
  map,
  of,
  startWith,
  Subject,
  switchMap,
} from 'rxjs';
import { FieldsService } from './generated/api/fields.service';
import { PlantingsService } from './generated/api/plantings.service';
import { FieldDto } from './generated/model/fieldDto';
import { PlantingRsDto } from './generated/model/plantingRsDto';
import { plantingDate, plantingStatusLabel } from './planting-display';

type LoadState<T> =
  | { status: 'loading' }
  | { status: 'ready'; data: T }
  | { status: 'error'; message: string };

@Component({
  selector: 'app-planting-list-page',
  imports: [RouterLink],
  template: `
    <header class="page-heading">
      <h1>作物栽培管理</h1>
      @if (selectedField(); as field) {
        @if (plantingState().status === 'ready' && !isEmpty()) {
          <a
            class="button"
            [routerLink]="['/dashboard/plantings', field.id, 'new']"
            >新增栽培紀錄</a
          >
        }
      }
    </header>

    @let fields = fieldState();
    @switch (fields.status) {
      @case ('loading') {
        <p class="panel empty-state" role="status">正在載入田區…</p>
      }
      @case ('error') {
        <section class="panel empty-state">
          <p class="error" role="alert">{{ fields.message }}</p>
          <button
            class="button button-outline"
            type="button"
            (click)="reloadFields()"
          >
            重新載入
          </button>
        </section>
      }
      @case ('ready') {
        @if (fields.data.length === 0) {
          <section class="panel empty-state">
            <h2>目前還沒有田區</h2>
            <a class="button" routerLink="/dashboard/fields/new">新增田區</a>
          </section>
        } @else {
          <div class="field-control planting-field-picker">
            <label for="planting-field">田區</label>
            <select id="planting-field" (change)="selectField($event)">
              <option value="" [selected]="!selectedField()">請選擇田區</option>
              @for (field of fields.data; track field.id) {
                <option
                  [value]="field.id"
                  [selected]="field.id === selectedField()?.id"
                >
                  {{ field.name }}
                </option>
              }
            </select>
          </div>
          @if (selectedField()) {
            @if (savedFieldId === selectedField()?.id) {
              <p class="field-success" role="status">已新增栽培紀錄</p>
            }
            @let list = plantingState();
            @switch (list.status) {
              @case ('loading') {
                <p class="panel empty-state" role="status">正在載入栽培紀錄…</p>
              }
              @case ('error') {
                <section class="panel empty-state">
                  <p class="error" role="alert">{{ list.message }}</p>
                  <button
                    class="button button-outline"
                    type="button"
                    (click)="reloadPlantings()"
                  >
                    重新載入
                  </button>
                </section>
              }
              @case ('ready') {
                @if (list.data.length === 0) {
                  <section class="panel empty-state">
                    <h2>這個田區還沒有栽培紀錄</h2>
                    <a
                      class="button"
                      [routerLink]="[
                        '/dashboard/plantings',
                        selectedField()?.id,
                        'new',
                      ]"
                      >新增栽培紀錄</a
                    >
                  </section>
                } @else {
                  <div class="panel field-table">
                    <table aria-label="栽培紀錄">
                      <thead>
                        <tr>
                          <th scope="col">作物</th>
                          <th scope="col">品種</th>
                          <th scope="col">狀態</th>
                          <th scope="col">栽種日期</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (planting of list.data; track planting.id) {
                          <tr>
                            <th scope="row">
                              {{ planting.cropName }}
                              @if (planting.notes) {
                                <p class="field-notes">{{ planting.notes }}</p>
                              }
                            </th>
                            <td>{{ planting.variety || '—' }}</td>
                            <td>{{ statusLabel[planting.status] }}</td>
                            <td>{{ dateLabel(planting) }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                }
              }
            }
          } @else {
            <p class="panel empty-state">請先選擇田區以查看栽培紀錄。</p>
          }
        }
      }
    }
  `,
})
export class PlantingListPage {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fields = inject(FieldsService);
  private readonly plantings = inject(PlantingsService);
  private readonly fieldReload = new Subject<void>();
  private readonly plantingReload = new Subject<void>();
  private readonly fieldIdChanges = this.route.queryParamMap.pipe(
    map((params) => params.get('fieldId')),
    distinctUntilChanged(),
  );
  private readonly fieldId = toSignal(this.fieldIdChanges, {
    initialValue: null,
  });
  protected readonly statusLabel = plantingStatusLabel;
  protected readonly dateLabel = plantingDate;
  protected readonly savedFieldId =
    this.router.currentNavigation()?.extras.info;

  protected readonly fieldState = toSignal(
    this.fieldReload.pipe(
      startWith(undefined),
      switchMap(() =>
        this.fields.getFields().pipe(
          map(({ fields }): LoadState<FieldDto[]> => ({
            status: 'ready',
            data: fields,
          })),
          startWith({ status: 'loading' } as const),
          catchError((error: unknown) =>
            of(this.loadError(error, '目前無法載入田區。')),
          ),
        ),
      ),
    ),
    { initialValue: { status: 'loading' } },
  );

  protected readonly selectedField = computed(() => {
    const state = this.fieldState();
    return state.status === 'ready'
      ? state.data.find((field) => field.id === this.fieldId())
      : undefined;
  });

  protected readonly plantingState = toSignal(
    combineLatest([
      this.fieldIdChanges,
      this.plantingReload.pipe(startWith(undefined)),
    ]).pipe(
      switchMap(([fieldId]) =>
        fieldId
          ? this.plantings.listFieldPlantings({ fieldId }).pipe(
              map((data): LoadState<PlantingRsDto[]> => ({
                status: 'ready',
                data,
              })),
              startWith({ status: 'loading' } as const),
              catchError((error: unknown) =>
                of(this.loadError(error, '目前無法載入栽培紀錄。')),
              ),
            )
          : of({ status: 'loading' } as const),
      ),
    ),
    { initialValue: { status: 'loading' } },
  );

  protected readonly isEmpty = computed(() => {
    const state = this.plantingState();
    return state.status === 'ready' && state.data.length === 0;
  });

  protected selectField(event: Event): void {
    const fieldId = (event.target as HTMLSelectElement).value;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { fieldId: fieldId || null },
    });
  }

  protected reloadFields(): void {
    this.fieldReload.next();
  }

  protected reloadPlantings(): void {
    this.plantingReload.next();
  }

  private loadError(
    error: unknown,
    message: string,
  ): { status: 'error'; message: string } {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl('/login', { replaceUrl: true });
    }
    return { status: 'error', message };
  }
}
