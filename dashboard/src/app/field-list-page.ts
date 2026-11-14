import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { FieldsService } from './generated/api/fields.service';
import { OverviewService } from './generated/api/overview.service';
import { FieldDto } from './generated/model/fieldDto';
import { PlantingRsDto } from './generated/model/plantingRsDto';
import { plantingDate, plantingStatusLabel } from './planting-display';

type ListedField = FieldDto & { plantings?: PlantingRsDto[] };
type FieldSortColumn = 'name' | 'areaSquareMeters';
type FieldSortDirection = 'ascending' | 'descending';

const fieldNameOrder = new Intl.Collator('zh-Hant-TW');

type ListState =
  | { status: 'loading' }
  | { status: 'error'; message: string; retry: boolean }
  | { status: 'ready'; fields: ListedField[] };

@Component({
  selector: 'app-field-list-page',
  imports: [DecimalPipe, RouterLink],
  styles: `
    .field-search {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: .65rem;
      margin-top: 1.5rem;
    }
    .field-search .field-control { width: min(100%, 22rem); }
    .field-search + .field-table { margin-top: .75rem; }
    .field-sort {
      padding: .25rem .5rem;
      margin: -.25rem -.5rem;
      border: 0;
      border-radius: .35rem;
      color: inherit;
      background: transparent;
      font-weight: inherit;
      white-space: nowrap;
      cursor: pointer;
    }
    .field-sort:hover, th[aria-sort] .field-sort {
      color: var(--green);
      background: var(--pale);
    }
    .field-no-matches { text-align: center; }
  `,
  template: `
    <header class="page-heading">
      <div>
        <h1>{{ overview ? '田區總覽' : '田區管理' }}</h1>
        <p>
          {{
            overview ? '查看各田區的規劃、栽培與採收狀況' : '查看自己的田區資料'
          }}
        </p>
      </div>
      @if (!overview && state().status === 'ready') {
        <div class="field-row-actions">
          <a class="button button-outline" routerLink="/dashboard/plantings">前往作物栽培管理</a>
          <a class="button" routerLink="/dashboard/fields/new">＋ 新增田區</a>
        </div>
      }
    </header>

    @if (notice()) {
      <p class="field-success" role="status">{{ notice() }}</p>
    }

    @let result = state();
    @switch (result.status) {
      @case ('loading') {
        <p class="panel empty-state" role="status">正在載入田區…</p>
      }
      @case ('error') {
        <section class="panel empty-state">
          <p class="error" role="alert">{{ result.message }}</p>
          @if (result.retry) {
            <button
              class="button button-outline"
              type="button"
              (click)="load()"
            >
              重新載入
            </button>
          }
        </section>
      }
      @case ('ready') {
        @if (result.fields.length === 0) {
          <section class="panel empty-state">
            <span class="empty-icon" aria-hidden="true">田</span>
            <h2>目前還沒有田區</h2>
            <p>先新增一塊田，就可以開始記錄要種的作物。</p>
            @if (overview) {
              <a class="button" routerLink="/dashboard/fields/new"
                >＋ 新增田區</a
              >
            }
          </section>
        } @else {
          @if (!overview) {
            <div class="field-search" role="search" aria-label="搜尋田區">
              <div class="field-control">
                <input
                  #fieldSearch
                  type="search"
                  aria-label="搜尋田區名稱"
                  placeholder="搜尋田區名稱"
                  [value]="searchText()"
                  (input)="searchText.set(fieldSearch.value)"
                />
              </div>
              @if (searchText()) {
                <button
                  class="button button-outline"
                  type="button"
                  (click)="searchText.set(''); fieldSearch.focus()"
                >
                  清除搜尋
                </button>
              }
              <p role="status">
                @if (searchTerm()) { {{ visibleFields().length }} 筆結果 }
              </p>
            </div>
          }
          <div class="panel field-table">
            <table [attr.aria-label]="overview ? '田區總覽' : '田區管理列表'">
              <thead>
                <tr>
                  @if (overview) {
                    <th scope="col">田區名稱</th>
                    <th scope="col">面積</th>
                  } @else {
                    <th scope="col" [attr.aria-sort]="sortDirection('name')">
                      <button class="field-sort" type="button" (click)="sortBy('name')">
                        田區名稱 <span aria-hidden="true">{{ sortIndicator('name') }}</span>
                      </button>
                    </th>
                    <th scope="col" [attr.aria-sort]="sortDirection('areaSquareMeters')">
                      <button class="field-sort" type="button" (click)="sortBy('areaSquareMeters')">
                        面積 <span aria-hidden="true">{{ sortIndicator('areaSquareMeters') }}</span>
                      </button>
                    </th>
                  }
                  <th scope="col">座標（緯度、經度）</th>
                  @if (overview) {
                    <th scope="col">目前栽培</th>
                  } @else {
                    <th scope="col">操作</th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (field of visibleFields(); track field.id) {
                  <tr>
                    <th scope="row">
                      {{ field.name }}
                      @if (field.notes) {
                        <p class="field-notes">{{ field.notes }}</p>
                      }
                    </th>
                    <td>{{ field.areaSquareMeters | number: '1.0-4' }} m²</td>
                    <td>
                      @if (field.latitude != null && field.longitude != null) {
                        {{ field.latitude | number: '1.6-6' }},
                        {{ field.longitude | number: '1.6-6' }}
                      } @else {
                        未設定
                      }
                    </td>
                    @if (overview) {
                      <td>
                        @for (planting of field.plantings; track planting.id) {
                          <div class="overview-planting">
                            <strong>{{ planting.cropName }}</strong>
                            @if (planting.variety) { <span>（{{ planting.variety }}）</span> }
                            <span>{{ statusLabel[planting.status] }}</span>
                            <p>{{ dateLabel(planting) }}</p>
                          </div>
                        } @empty {
                          目前沒有栽培紀錄
                        }
                      </td>
                    } @else {
                      <td>
                        <div class="field-row-actions">
                          <a
                            class="button button-outline"
                            [routerLink]="[
                              '/dashboard/fields',
                              field.id,
                              'edit',
                            ]"
                            [attr.aria-label]="'編輯田區：' + field.name"
                            >編輯</a
                          >
                          <button
                            class="button button-outline"
                            type="button"
                            [attr.aria-label]="'刪除田區：' + field.name"
                            (click)="confirmDelete(field, deleteDialog)"
                          >
                            刪除
                          </button>
                        </div>
                      </td>
                    }
                  </tr>
                } @empty {
                  <tr><td colspan="4" class="field-no-matches">找不到符合的田區。</td></tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    }

    <dialog
      #deleteDialog
      class="field-delete-dialog"
      aria-labelledby="field-delete-title"
      aria-describedby="field-delete-description"
      (cancel)="onCancelDelete($event)"
    >
      <h2 id="field-delete-title">刪除田區</h2>
      <p id="field-delete-description">
        確定刪除「{{ deletingField()?.name }}」？此操作無法復原。
      </p>
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
          [disabled]="deleting()"
          (click)="deleteField(deleteDialog)"
        >
          {{ deleting() ? '刪除中…' : '確認刪除' }}
        </button>
      </div>
    </dialog>
  `,
})
export class FieldListPage {
  private readonly fields = inject(FieldsService);
  private readonly overviewApi = inject(OverviewService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  protected readonly overview =
    inject(ActivatedRoute).snapshot.data['overview'] === true;
  protected readonly state = signal<ListState>({ status: 'loading' });
  protected readonly searchText = signal('');
  protected readonly searchTerm = computed(() => this.searchText().trim().toLowerCase());
  private readonly sort = signal<{ column: FieldSortColumn; direction: FieldSortDirection }>({
    column: 'name',
    direction: 'ascending',
  });
  protected readonly visibleFields = computed(() => {
    const result = this.state();
    if (result.status !== 'ready') return [];
    if (this.overview) return result.fields;

    const term = this.searchTerm();
    const { column, direction } = this.sort();
    return result.fields
      .filter((field) => field.name.toLowerCase().includes(term))
      .sort((left, right) => {
        const byName = fieldNameOrder.compare(left.name, right.name);
        const order = column === 'name'
          ? byName
          : left.areaSquareMeters - right.areaSquareMeters;
        return order === 0 ? byName : direction === 'ascending' ? order : -order;
      });
  });
  protected readonly notice = signal(
    fieldSaveNotice(this.router.currentNavigation()?.extras.info),
  );
  protected readonly deletingField = signal<FieldDto | null>(null);
  protected readonly deleting = signal(false);
  protected readonly statusLabel = plantingStatusLabel;
  protected readonly dateLabel = plantingDate;
  protected readonly deleteError = signal('');

  ngOnInit(): void {
    this.load();
  }

  protected sortBy(column: FieldSortColumn): void {
    this.sort.update((current) => ({
      column,
      direction: current.column === column && current.direction === 'ascending'
        ? 'descending'
        : 'ascending',
    }));
  }

  protected sortDirection(column: FieldSortColumn): FieldSortDirection | null {
    const sort = this.sort();
    return sort.column === column ? sort.direction : null;
  }

  protected sortIndicator(column: FieldSortColumn): string {
    switch (this.sortDirection(column)) {
      case 'ascending': return '↑';
      case 'descending': return '↓';
      default: return '↕';
    }
  }

  protected load(): void {
    this.state.set({ status: 'loading' });
    const request = this.overview
      ? this.overviewApi.getOverview()
      : this.fields.getFields().pipe(map(({ fields }) => fields));
    request
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (fields) => this.state.set({ status: 'ready', fields }),
        error: (error: unknown) => {
          const status =
            error instanceof HttpErrorResponse ? error.status : undefined;
          if (status === 401) {
            void this.router.navigateByUrl('/login', { replaceUrl: true });
          } else if (status === 403) {
            this.state.set({
              status: 'error',
              message: '此帳號無法查看田區。',
              retry: false,
            });
          } else {
            this.state.set({
              status: 'error',
              message: '目前無法載入田區，請再試一次。',
              retry: true,
            });
          }
        },
      });
  }

  protected confirmDelete(field: FieldDto, dialog: HTMLDialogElement): void {
    this.deletingField.set(field);
    this.deleteError.set('');
    dialog.showModal();
  }

  protected onCancelDelete(event: Event): void {
    if (this.deleting()) event.preventDefault();
  }

  protected deleteField(dialog: HTMLDialogElement): void {
    const field = this.deletingField();
    if (!field || this.deleting()) return;
    this.deleting.set(true);
    this.deleteError.set('');
    this.fields
      .deleteField({ fieldId: field.id })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deleting.set(false);
          dialog.close();
          this.state.update((result) =>
            result.status === 'ready'
              ? {
                  status: 'ready',
                  fields: result.fields.filter((item) => item.id !== field.id),
                }
              : result,
          );
          this.notice.set(`已刪除田區「${field.name}」`);
        },
        error: (error: unknown) => {
          this.deleting.set(false);
          const status =
            error instanceof HttpErrorResponse ? error.status : undefined;
          if (status === 401) {
            dialog.close();
            void this.router.navigateByUrl('/login', { replaceUrl: true });
          } else if (status === 404) {
            this.deleteError.set('找不到此田區，請關閉後重新整理列表。');
          } else if (status === 403) {
            this.deleteError.set('此帳號無法刪除田區。');
          } else if (status === 409) {
            this.deleteError.set('有栽培紀錄的田區不能刪除。');
          } else {
            this.deleteError.set('目前無法刪除田區，請再試一次。');
          }
        },
      });
  }
}

function fieldSaveNotice(info: unknown): string {
  if (
    typeof info !== 'object' ||
    !info ||
    !('action' in info) ||
    !('name' in info) ||
    typeof info.name !== 'string'
  )
    return '';
  if (info.action === 'created') return `已新增田區「${info.name}」`;
  if (info.action === 'edited') return `已更新田區「${info.name}」`;
  return '';
}
