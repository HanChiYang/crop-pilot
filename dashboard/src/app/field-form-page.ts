import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FieldsService } from './generated/api/fields.service';
import { CreateFieldRqDto } from './generated/model/createFieldRqDto';
import { FieldDto } from './generated/model/fieldDto';

type FieldErrors = Partial<Record<keyof CreateFieldRqDto, string>>;

@Component({
  selector: 'app-field-form-page',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <header class="page-heading">
      <div>
        <h1>{{ title }}</h1>
        <p><a routerLink="/dashboard/fields">田區管理</a> / {{ title }}</p>
      </div>
    </header>

    @if (loadState() !== 'ready') {
      <section class="panel empty-state">
        @if (loadState() === 'loading') {
          <p role="status">正在載入田區…</p>
        } @else {
          <p class="error" role="alert">{{ message() }}</p>
          @if (loadState() === 'retry') {
            <button
              class="button button-outline"
              type="button"
              (click)="load()"
            >
              重新載入
            </button>
          }
          @if (sessionExpired()) {
            <a class="button button-outline" routerLink="/login">重新登入</a>
          }
        }
      </section>
    } @else {
      <form
        class="panel field-form"
        [formGroup]="form"
        (ngSubmit)="save()"
        novalidate
      >
        <fieldset [disabled]="saving() || saved()">
          <div class="field-form-grid">
            <div class="field-control">
              <label for="field-name">田區名稱（必填）</label>
              <input
                id="field-name"
                formControlName="name"
                required
                [attr.aria-invalid]="errors().name ? 'true' : null"
                aria-describedby="field-name-error"
              />
              <p id="field-name-error" class="error">{{ errors().name }}</p>
            </div>
            <div class="field-control">
              <label for="field-area">面積（平方公尺，必填）</label>
              <input
                id="field-area"
                formControlName="areaSquareMeters"
                inputmode="decimal"
                required
                [attr.aria-invalid]="errors().areaSquareMeters ? 'true' : null"
                aria-describedby="field-area-error"
              />
              <p id="field-area-error" class="error">
                {{ errors().areaSquareMeters }}
              </p>
            </div>
          </div>

          <fieldset
            class="field-coordinates"
            aria-describedby="field-coordinate-help"
          >
            <legend>田區座標（選填）</legend>
            <div class="field-form-grid">
              <div class="field-control">
                <label for="field-latitude">緯度</label>
                <input
                  id="field-latitude"
                  formControlName="latitude"
                  inputmode="decimal"
                  [attr.aria-invalid]="errors().latitude ? 'true' : null"
                  aria-describedby="field-latitude-error"
                />
                <p id="field-latitude-error" class="error">
                  {{ errors().latitude }}
                </p>
              </div>
              <div class="field-control">
                <label for="field-longitude">經度</label>
                <input
                  id="field-longitude"
                  formControlName="longitude"
                  inputmode="decimal"
                  [attr.aria-invalid]="errors().longitude ? 'true' : null"
                  aria-describedby="field-longitude-error"
                />
                <p id="field-longitude-error" class="error">
                  {{ errors().longitude }}
                </p>
              </div>
            </div>
            <p id="field-coordinate-help">
              填寫時請同時提供同一個定位點的緯度與經度。
            </p>
          </fieldset>

          <div class="field-control">
            <label for="field-notes">備註（選填）</label>
            <textarea
              id="field-notes"
              formControlName="notes"
              rows="3"
              [attr.aria-invalid]="errors().notes ? 'true' : null"
              aria-describedby="field-notes-error"
            ></textarea>
            <p id="field-notes-error" class="error">{{ errors().notes }}</p>
          </div>

          <div class="field-form-actions">
            <button class="button" type="submit">
              {{ saved() ? '已儲存' : saving() ? '儲存中…' : '儲存田區' }}
            </button>
            <button
              class="button button-outline"
              type="button"
              (click)="cancel()"
            >
              取消
            </button>
          </div>
        </fieldset>

        @if (message()) {
          <p class="error field-form-message" role="alert">{{ message() }}</p>
        }
        @if (sessionExpired()) {
          <a class="button button-outline" routerLink="/login">重新登入</a>
        }
        @if (saved()) {
          <a class="button button-outline" routerLink="/dashboard/fields"
            >前往田區管理</a
          >
        }
      </form>
    }
  `,
})
export class FieldFormPage {
  private readonly fields = inject(FieldsService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fieldId =
    inject(ActivatedRoute).snapshot.paramMap.get('fieldId');
  protected readonly title = this.fieldId ? '編輯田區' : '新增田區';
  protected readonly loadState = signal<
    'loading' | 'retry' | 'error' | 'ready'
  >(this.fieldId ? 'loading' : 'ready');

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: '',
    areaSquareMeters: '',
    latitude: '',
    longitude: '',
    notes: '',
  });
  protected readonly errors = signal<FieldErrors>({});
  protected readonly message = signal('');
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly sessionExpired = signal(false);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    if (!this.fieldId) return;
    this.loadState.set('loading');
    this.message.set('');
    this.fields
      .getField({ fieldId: this.fieldId })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (field) => {
          this.populateForm(field);
          this.loadState.set('ready');
        },
        error: (error: unknown) => {
          const status =
            error instanceof HttpErrorResponse ? error.status : undefined;
          this.sessionExpired.set(status === 401);
          if (
            status === 401 ||
            status === 403 ||
            status === 404 ||
            status === 400
          ) {
            this.loadState.set('error');
            this.message.set(
              status === 401
                ? '登入已逾時，請重新登入後再試。'
                : status === 403
                  ? '此帳號無法編輯田區。'
                  : '找不到此田區，請返回田區管理。',
            );
          } else {
            this.loadState.set('retry');
            this.message.set('目前無法載入田區，請再試一次。');
          }
        },
      });
  }

  private populateForm(field: FieldDto): void {
    this.form.setValue({
      name: field.name,
      areaSquareMeters: String(field.areaSquareMeters),
      latitude: field.latitude == null ? '' : String(field.latitude),
      longitude: field.longitude == null ? '' : String(field.longitude),
      notes: field.notes ?? '',
    });
  }

  protected cancel(): void {
    void this.router.navigateByUrl(
      this.fieldId ? '/dashboard/fields' : '/dashboard',
    );
  }

  protected save(): void {
    if (this.saving() || this.saved()) return;

    const values = this.form.getRawValue();
    const { name } = values;
    const area = values.areaSquareMeters.trim();
    const latitude = values.latitude.trim();
    const longitude = values.longitude.trim();
    const errors: FieldErrors = {};

    if (/^\p{White_Space}*$/u.test(name)) errors.name = '請輸入田區名稱。';
    if (!area) {
      errors.areaSquareMeters = '請輸入田區面積。';
    } else if (
      !isDecimal(area, 4) ||
      Number(area) <= 0 ||
      Number(area) > 9999999999.9999
    ) {
      errors.areaSquareMeters =
        '請輸入大於 0、至多 9,999,999,999.9999 的面積，最多四位小數。';
    }
    if (
      latitude &&
      (!isDecimal(latitude, 6) || Math.abs(Number(latitude)) > 90)
    ) {
      errors.latitude = '請輸入 -90 至 90 的緯度，最多六位小數。';
    } else if (!latitude && longitude) {
      errors.latitude = '請與經度一起填寫。';
    }
    if (
      longitude &&
      (!isDecimal(longitude, 6) || Math.abs(Number(longitude)) > 180)
    ) {
      errors.longitude = '請輸入 -180 至 180 的經度，最多六位小數。';
    } else if (!longitude && latitude) {
      errors.longitude = '請與緯度一起填寫。';
    }

    this.errors.set(errors);
    this.message.set('');
    this.sessionExpired.set(false);
    if (Object.keys(errors).length) {
      this.message.set('請檢查標示的欄位。');
      return;
    }

    const request: CreateFieldRqDto = {
      name,
      areaSquareMeters: Number(area),
      latitude: latitude ? Number(latitude) : null,
      longitude: longitude ? Number(longitude) : null,
      notes: this.fieldId ? values.notes || null : values.notes,
    };

    this.saving.set(true);
    const save = this.fieldId
      ? this.fields.editField({
          fieldId: this.fieldId,
          editFieldRqDto: request,
        })
      : this.fields.createField({ createFieldRqDto: request });
    save.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: ({ fields }) => {
        this.saved.set(true);
        this.saving.set(false);
        void this.router.navigateByUrl('/dashboard/fields', {
          info: {
            action: this.fieldId ? 'edited' : 'created',
            name: fields[0]?.name,
          },
        });
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.showSaveError(error);
      },
    });
  }

  private showSaveError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 400 || error.status === 409) {
        const errors: FieldErrors = {};
        const problem = (
          error.error as { errors?: Record<string, unknown> } | null
        )?.errors;
        for (const field of Object.keys(
          this.form.controls,
        ) as (keyof CreateFieldRqDto)[]) {
          const messages = problem?.[field];
          if (Array.isArray(messages) && typeof messages[0] === 'string') {
            errors[field] = messages[0];
          }
        }
        if (error.status === 409 && !errors.name)
          errors.name = '此田區名稱已存在。';
        if (Object.keys(errors).length) {
          this.errors.set(errors);
          this.message.set('請檢查標示的欄位。');
          return;
        }
      } else if (error.status === 401) {
        this.message.set('登入已逾時，請重新登入後再試。');
        this.sessionExpired.set(true);
        return;
      } else if (error.status === 403) {
        this.message.set(`此帳號無法${this.title}。`);
        return;
      } else if (error.status === 404 && this.fieldId) {
        this.message.set('找不到此田區，請返回田區管理。');
        return;
      }
    }
    this.message.set('目前無法儲存田區，請再試一次。');
  }
}

function isDecimal(value: string, scale: number): boolean {
  const match = /^[+-]?(?:\d+(?:\.(\d*))?|\.(\d+))$/.exec(value);
  const fraction = match?.[1] ?? match?.[2] ?? '';
  return match !== null && fraction.replace(/0+$/, '').length <= scale;
}
