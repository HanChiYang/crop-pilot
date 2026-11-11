import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { FieldsService } from './generated/api/fields.service';
import { PlantingsService } from './generated/api/plantings.service';
import { CreatePlantingRqDto } from './generated/model/createPlantingRqDto';
import { FieldDto } from './generated/model/fieldDto';
import { plantingStatusLabel } from './planting-display';

type PlantingErrors = Partial<Record<keyof CreatePlantingRqDto, string[]>>;

@Component({
  selector: 'app-planting-form-page',
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <header class="page-heading">
      <div>
        <h1>新增栽培紀錄</h1>
        @if (field(); as selected) {
          <p>田區　{{ selected.name }}</p>
        }
      </div>
    </header>

    @if (field()) {
      <form
        class="panel field-form"
        [formGroup]="form"
        (ngSubmit)="save()"
        novalidate
      >
        <fieldset [disabled]="saving() || saved()">
          <div class="field-form-grid">
            <div class="field-control">
              <label for="crop-name">作物名稱（必填）</label>
              <input
                id="crop-name"
                formControlName="cropName"
                required
                [attr.aria-invalid]="errors().cropName ? 'true' : null"
                aria-describedby="crop-name-error"
              />
              <p class="error" id="crop-name-error">
                {{ errors().cropName?.[0] }}
              </p>
            </div>
            <div class="field-control">
              <label for="variety">品種（選填）</label>
              <input
                id="variety"
                formControlName="variety"
                [attr.aria-invalid]="errors().variety ? 'true' : null"
                aria-describedby="variety-error"
              />
              <p class="error" id="variety-error">
                {{ errors().variety?.[0] }}
              </p>
            </div>
            <div class="field-control">
              <label for="planting-status">狀態</label>
              <select
                id="planting-status"
                formControlName="status"
                aria-describedby="planting-status-error"
              >
                @for (status of statuses; track status) {
                  <option [value]="status">{{ statusLabel[status] }}</option>
                }
              </select>
              <p class="error" id="planting-status-error">
                {{ errors().status?.[0] }}
              </p>
            </div>
            <div class="field-control">
              <label for="planned-date">預定栽種日（選填）</label>
              <input
                id="planned-date"
                type="date"
                formControlName="plannedPlantingDate"
                [attr.aria-invalid]="
                  errors().plannedPlantingDate ? 'true' : null
                "
                aria-describedby="planned-date-error"
              />
              <p class="error" id="planned-date-error">
                {{ errors().plannedPlantingDate?.[0] }}
              </p>
            </div>
            @if (status() !== 'planned') {
              <div class="field-control">
                <label for="actual-date"
                  >實際栽種日（{{
                    status() === 'growing' ? '必填' : '選填'
                  }}）</label
                >
                <input
                  id="actual-date"
                  type="date"
                  formControlName="actualPlantingDate"
                  [required]="status() === 'growing'"
                  [attr.aria-invalid]="
                    errors().actualPlantingDate ? 'true' : null
                  "
                  aria-describedby="actual-date-error"
                />
                <p class="error" id="actual-date-error">
                  {{ errors().actualPlantingDate?.[0] }}
                </p>
              </div>
            }
          </div>
          <div class="field-control planting-form-notes">
            <label for="planting-notes">備註（選填）</label>
            <textarea
              id="planting-notes"
              rows="3"
              formControlName="notes"
              [attr.aria-invalid]="errors().notes ? 'true' : null"
              aria-describedby="planting-notes-error"
            ></textarea>
            <p class="error" id="planting-notes-error">
              {{ errors().notes?.[0] }}
            </p>
          </div>
          <div class="field-form-actions">
            <button class="button" type="submit">
              {{ saving() ? '儲存中…' : '儲存' }}
            </button>
            <button
              class="button button-outline"
              type="button"
              (click)="back()"
            >
              取消
            </button>
          </div>
        </fieldset>
        @if (message()) {
          <p class="error field-form-message" role="alert">{{ message() }}</p>
        }
        @if (saved()) {
          <a routerLink="/dashboard/plantings" [queryParams]="{ fieldId }"
            >返回作物栽培管理</a
          >
        }
      </form>
    } @else if (message()) {
      <section class="panel empty-state">
        <p class="error" role="alert">{{ message() }}</p>
        <button
          class="button button-outline"
          type="button"
          (click)="loadField()"
        >
          重新載入
        </button>
        <a routerLink="/dashboard/plantings">返回作物栽培管理</a>
      </section>
    } @else {
      <p class="panel empty-state" role="status">正在載入田區…</p>
    }
  `,
})
export class PlantingFormPage {
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fields = inject(FieldsService);
  private readonly plantings = inject(PlantingsService);
  protected readonly fieldId =
    inject(ActivatedRoute).snapshot.paramMap.get('fieldId')!;
  protected readonly field = signal<FieldDto | null>(null);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly errors = signal<PlantingErrors>({});
  protected readonly message = signal('');
  protected readonly statuses = Object.values(CreatePlantingRqDto.StatusEnum);
  protected readonly statusLabel = plantingStatusLabel;
  protected readonly form = inject(FormBuilder).nonNullable.group({
    cropName: '',
    variety: '',
    status: 'planned' as CreatePlantingRqDto.StatusEnum,
    plannedPlantingDate: '',
    actualPlantingDate: '',
    notes: '',
  });
  protected readonly status = toSignal(this.form.controls.status.valueChanges, {
    initialValue: 'planned',
  });

  ngOnInit(): void {
    this.loadField();
  }

  protected loadField(): void {
    this.message.set('');
    this.fields
      .getField({ fieldId: this.fieldId })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (field) => this.field.set(field),
        error: (error: unknown) =>
          this.showFailure(error, '目前無法載入田區，請再試一次。'),
      });
  }

  protected save(): void {
    if (this.saving() || this.saved() || !this.field()) return;
    this.message.set('');
    const values = this.form.getRawValue();
    const cropName = values.cropName.trim();
    const errors: PlantingErrors = {};
    if (!cropName) errors.cropName = ['請輸入作物名稱。'];
    if (values.status === 'growing' && !values.actualPlantingDate) {
      errors.actualPlantingDate = ['請填寫實際栽種日。'];
    }
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.message.set('請檢查標示的欄位。');
      return;
    }

    this.saving.set(true);
    this.plantings
      .createPlanting({
        fieldId: this.fieldId,
        createPlantingRqDto: {
          cropName,
          variety: values.variety.trim() || null,
          status: values.status,
          plannedPlantingDate: values.plannedPlantingDate || null,
          actualPlantingDate:
            values.status === 'planned'
              ? null
              : values.actualPlantingDate || null,
          notes: values.notes.trim() || null,
        },
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: () => {
          this.saved.set(true);
          this.back(true);
        },
        error: (error: unknown) => {
          if (
            error instanceof HttpErrorResponse &&
            error.status === 400 &&
            error.error?.errors
          ) {
            this.errors.set(error.error.errors);
            this.message.set('請檢查標示的欄位。');
          } else {
            this.showFailure(error, '目前無法儲存栽培紀錄，請再試一次。');
          }
        },
      });
  }

  protected back(saved = false): void {
    void this.router.navigate(['/dashboard/plantings'], {
      queryParams: { fieldId: this.fieldId },
      info: saved ? this.fieldId : undefined,
    });
  }

  private showFailure(error: unknown, message: string): void {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl('/login', { replaceUrl: true });
    } else {
      this.message.set(
        error instanceof HttpErrorResponse && error.status === 404
          ? '找不到可存取的田區。'
          : message,
      );
    }
  }
}
