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

    @if (createdFieldName) {
      <p class="field-success" role="status">
        已新增田區「{{ createdFieldName }}」
      </p>
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
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    }
  `,
})
export class FieldListPage {
  private readonly fields = inject(FieldsService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly navigationInfo =
    this.router.currentNavigation()?.extras.info;

  protected readonly overview =
    inject(ActivatedRoute).snapshot.data['overview'] === true;
  protected readonly createdFieldName =
    typeof this.navigationInfo === 'string' ? this.navigationInfo : undefined;
  protected readonly state = signal<ListState>({ status: 'loading' });

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
}
