import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FieldsService } from './generated/api/fields.service';
import { FieldDto } from './generated/model/fieldDto';

type ListState =
  | { status: 'loading' }
  | { status: 'error'; message: string; retry: boolean }
  | { status: 'ready'; fields: FieldDto[] };

@Component({
  selector: 'app-field-list-page',
  imports: [DecimalPipe, RouterLink],
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
        <a class="button" routerLink="/dashboard/fields/new">＋ 新增田區</a>
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
          <div class="panel field-table">
            <table [attr.aria-label]="overview ? '田區總覽' : '田區管理列表'">
              <thead>
                <tr>
                  <th scope="col">田區名稱</th>
                  <th scope="col">面積</th>
                  <th scope="col">座標（緯度、經度）</th>
                  @if (!overview) {
                    <th scope="col">操作</th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (field of result.fields; track field.id) {
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
                    @if (!overview) {
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
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  protected readonly overview =
    inject(ActivatedRoute).snapshot.data['overview'] === true;
  protected readonly state = signal<ListState>({ status: 'loading' });
  protected readonly notice = signal(
    fieldSaveNotice(this.router.currentNavigation()?.extras.info),
  );
  protected readonly deletingField = signal<FieldDto | null>(null);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal('');

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.state.set({ status: 'loading' });
    this.fields
      .getFields()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ fields }) => this.state.set({ status: 'ready', fields }),
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
