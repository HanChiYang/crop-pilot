import { HttpErrorResponse } from "@angular/common/http";
import { DecimalPipe } from "@angular/common";
import { Component, DestroyRef, computed, inject, signal } from "@angular/core";
import { takeUntilDestroyed, toSignal } from "@angular/core/rxjs-interop";
import { ActivatedRoute, Router, RouterLink } from "@angular/router";
import {
  catchError,
  combineLatest,
  distinctUntilChanged,
  finalize,
  map,
  of,
  startWith,
  Subject,
  switchMap,
} from "rxjs";
import { FieldsService } from "./generated/api/fields.service";
import { PlantingsService } from "./generated/api/plantings.service";
import { FieldDto } from "./generated/model/fieldDto";
import { FieldPlantingsRsDto } from "./generated/model/fieldPlantingsRsDto";
import { PlantingRsDto } from "./generated/model/plantingRsDto";
import { plantingDate, plantingStatusLabel } from "./planting-display";

type LoadState<T> =
  | { status: "loading" }
  | { status: "ready"; data: T }
  | { status: "error"; message: string };

@Component({
  selector: "app-planting-list-page",
  imports: [DecimalPipe, RouterLink],
  template: `
    <header class="page-heading">
      <h1>作物栽培管理</h1>
      @if (selectedField(); as field) {
        @if (plantingState().status === "ready" && !isEmpty()) {
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
      @case ("loading") {
        <p class="panel empty-state" role="status">正在載入田區…</p>
      }
      @case ("error") {
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
      @case ("ready") {
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
            @if (notice()?.fieldId === selectedField()?.id) {
              <p class="field-success" role="status">{{ notice()?.text }}</p>
            }
            @let list = plantingState();
            @switch (list.status) {
              @case ("loading") {
                <p class="panel empty-state" role="status">正在載入栽培紀錄…</p>
              }
              @case ("error") {
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
              @case ("ready") {
                <dl class="panel planting-area-summary">
                  <div>
                    <dt>田區總面積</dt>
                    <dd>
                      {{ list.data.areaSquareMeters | number: "1.0-4" }} m²
                    </dd>
                  </div>
                  <div>
                    <dt>已分配面積</dt>
                    <dd>
                      {{
                        list.data.allocatedAreaSquareMeters | number: "1.0-4"
                      }}
                      m²
                    </dd>
                  </div>
                  <div>
                    <dt>剩餘面積</dt>
                    <dd>
                      {{
                        list.data.remainingAreaSquareMeters | number: "1.0-4"
                      }}
                      m²
                    </dd>
                  </div>
                </dl>
                @if (list.data.plantings.length === 0) {
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
                          <th scope="col">占用面積</th>
                          <th scope="col">操作</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (
                          planting of list.data.plantings;
                          track planting.id
                        ) {
                          <tr>
                            <th scope="row">
                              {{ planting.cropName }}
                              @if (planting.notes) {
                                <p class="field-notes">{{ planting.notes }}</p>
                              }
                            </th>
                            <td>{{ planting.variety || "—" }}</td>
                            <td>{{ statusLabel[planting.status] }}</td>
                            <td>{{ dateLabel(planting) }}</td>
                            <td>
                              {{
                                planting.areaSquareMeters | number: "1.0-4"
                              }}
                              m²
                            </td>
                            <td class="planting-actions">
                              <details class="planting-operation-menu">
                                <summary [attr.aria-label]="'田間作業 ' + planting.cropName">⋯</summary>
                                <a [routerLink]="['/dashboard/plantings', planting.fieldId, planting.id, 'fertilizations']">
                                  施肥紀錄
                                </a>
                              </details>
                              <a
                                [routerLink]="[
                                  '/dashboard/plantings',
                                  planting.fieldId,
                                  planting.id,
                                  'edit',
                                ]"
                                [attr.aria-label]="'編輯 ' + planting.cropName"
                                >編輯</a
                              >
                              @if (planting.status === "planned") {
                                <button
                                  type="button"
                                  class="button button-outline"
                                  [attr.aria-label]="
                                    '刪除 ' + planting.cropName
                                  "
                                  (click)="
                                    confirmDelete(planting, deleteDialog)
                                  "
                                >
                                  刪除
                                </button>
                              }
                            </td>
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
    <dialog
      #deleteDialog
      class="field-delete-dialog"
      aria-labelledby="planting-delete-title"
      (cancel)="deleting() && $event.preventDefault()"
    >
      <h2 id="planting-delete-title">刪除栽培紀錄</h2>
      <p>確定刪除「{{ pendingDelete()?.cropName }}」？</p>
      @if (deleteError()) {
        <p class="error" role="alert">{{ deleteError() }}</p>
      }
      <div class="field-form-actions">
        <button
          class="button button-outline"
          type="button"
          autofocus
          [disabled]="deleting()"
          (click)="deleteDialog.close()"
        >
          取消
        </button>
        <button
          class="button"
          type="button"
          [disabled]="deleting() || deleteConflict()"
          (click)="deletePlanting(deleteDialog)"
        >
          {{ deleting() ? "刪除中…" : "確認刪除" }}
        </button>
      </div>
    </dialog>
  `,
  styles: `
    .planting-area-summary {
      display: flex;
      flex-wrap: wrap;
      gap: 1rem 3rem;
      padding: 1rem 1.25rem;
    }
    .planting-area-summary dd {
      margin: 0.25rem 0 0;
      font-weight: 700;
    }
    .planting-actions {
      white-space: nowrap;
    }
    .planting-operation-menu {
      display: inline-block;
      position: relative;
      margin-inline-end: .5rem;
    }
    .planting-operation-menu summary { cursor: pointer; list-style: none; padding: .35rem; }
    .planting-operation-menu a { display: block; white-space: nowrap; }
    .planting-actions button {
      margin-inline-start: 0.5rem;
    }
  `,
})
export class PlantingListPage {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fields = inject(FieldsService);
  private readonly plantings = inject(PlantingsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fieldReload = new Subject<void>();
  private readonly plantingReload = new Subject<void>();
  private readonly fieldIdChanges = this.route.queryParamMap.pipe(
    map((params) => params.get("fieldId")),
    distinctUntilChanged(),
  );
  private readonly fieldId = toSignal(this.fieldIdChanges, {
    initialValue: null,
  });
  protected readonly statusLabel = plantingStatusLabel;
  protected readonly dateLabel = plantingDate;
  private readonly saveInfo = this.router.currentNavigation()?.extras.info as
    { fieldId: string; edited: boolean } | undefined;
  protected readonly notice = signal(
    this.saveInfo
      ? {
          fieldId: this.saveInfo.fieldId,
          text: this.saveInfo.edited ? "已更新栽培紀錄" : "已新增栽培紀錄",
        }
      : null,
  );
  protected readonly pendingDelete = signal<PlantingRsDto | null>(null);
  protected readonly deleteError = signal("");
  protected readonly deleteConflict = signal(false);
  protected readonly deleting = signal(false);

  protected readonly fieldState = toSignal(
    this.fieldReload.pipe(
      startWith(undefined),
      switchMap(() =>
        this.fields.getFields().pipe(
          map(({ fields }): LoadState<FieldDto[]> => ({
            status: "ready",
            data: fields,
          })),
          startWith({ status: "loading" } as const),
          catchError((error: unknown) =>
            of(this.loadError(error, "目前無法載入田區。")),
          ),
        ),
      ),
    ),
    { initialValue: { status: "loading" } },
  );

  protected readonly selectedField = computed(() => {
    const state = this.fieldState();
    return state.status === "ready"
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
              map((data): LoadState<FieldPlantingsRsDto> => ({
                status: "ready",
                data,
              })),
              startWith({ status: "loading" } as const),
              catchError((error: unknown) =>
                of(this.loadError(error, "目前無法載入栽培紀錄。")),
              ),
            )
          : of({ status: "loading" } as const),
      ),
    ),
    { initialValue: { status: "loading" } },
  );

  protected readonly isEmpty = computed(() => {
    const state = this.plantingState();
    return state.status === "ready" && state.data.plantings.length === 0;
  });

  protected selectField(event: Event): void {
    const fieldId = (event.target as HTMLSelectElement).value;
    this.notice.set(null);
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

  protected confirmDelete(
    planting: PlantingRsDto,
    dialog: HTMLDialogElement,
  ): void {
    this.pendingDelete.set(planting);
    this.deleteError.set("");
    this.deleteConflict.set(false);
    dialog.showModal();
  }

  protected deletePlanting(dialog: HTMLDialogElement): void {
    const planting = this.pendingDelete();
    if (!planting || this.deleting() || this.deleteConflict()) return;
    this.deleting.set(true);
    this.deleteError.set("");
    this.plantings
      .deletePlanting({ fieldId: planting.fieldId, plantingId: planting.id })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.deleting.set(false)),
      )
      .subscribe({
        next: () => {
          dialog.close();
          this.notice.set({
            fieldId: planting.fieldId,
            text: "已刪除栽培紀錄",
          });
          this.reloadPlantings();
        },
        error: (error: unknown) => {
          if (
            error instanceof HttpErrorResponse &&
            (error.status === 404 || error.status === 409)
          ) {
            this.deleteConflict.set(true);
            this.deleteError.set(
              error.status === 409
                ? "只有規劃中的栽培紀錄可以刪除。"
                : "找不到可刪除的栽培紀錄。",
            );
            this.reloadPlantings();
            return;
          }
          this.deleteError.set(
            this.loadError(error, "目前無法刪除栽培紀錄，請再試一次。").message,
          );
        },
      });
  }

  private loadError(
    error: unknown,
    message: string,
  ): { status: "error"; message: string } {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl("/login", { replaceUrl: true });
    }
    return { status: "error", message };
  }
}
