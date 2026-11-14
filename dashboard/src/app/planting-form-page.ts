import { HttpErrorResponse } from "@angular/common/http";
import { DecimalPipe } from "@angular/common";
import { Component, DestroyRef, computed, inject, signal } from "@angular/core";
import { takeUntilDestroyed, toObservable, toSignal } from "@angular/core/rxjs-interop";
import { FormBuilder, ReactiveFormsModule } from "@angular/forms";
import { ActivatedRoute, Router, RouterLink } from "@angular/router";
import { catchError, finalize, forkJoin, map, of, startWith, switchMap } from "rxjs";
import { FieldsService } from "./generated/api/fields.service";
import { PlantingsService } from "./generated/api/plantings.service";
import { CreatePlantingRqDto } from "./generated/model/createPlantingRqDto";
import { FieldDto } from "./generated/model/fieldDto";
import { EditPlantingRqDto } from "./generated/model/editPlantingRqDto";
import { PlantingStatus } from "./generated/model/plantingStatus";
import { plantingStatusLabel } from "./planting-display";

type PlantingErrors = Partial<Record<keyof EditPlantingRqDto, string[]>>;

@Component({
  selector: "app-planting-form-page",
  imports: [DecimalPipe, ReactiveFormsModule, RouterLink],
  template: `
    <header class="page-heading">
      <div>
        <h1>{{ plantingId ? "編輯栽培紀錄" : "新增栽培紀錄" }}</h1>
        @if (fixedFieldId && field(); as selected) {
          <p>田區　{{ selected.name }}</p>
        }
      </div>
    </header>

    @if (!fixedFieldId && availableFields(); as fields) {
      <div class="field-control planting-field-picker">
        <label for="new-planting-field">選擇田區</label>
        <select #picker id="new-planting-field" [disabled]="saving() || saved() || !fields.length"
          (change)="selectField(picker.value)">
          <option value="" [selected]="!field()">請選擇田區</option>
          @for (item of fields; track item.id) {
            <option [value]="item.id" [selected]="field()?.id === item.id">{{ item.name }}</option>
          }
        </select>
      </div>
      @if (!fields.length) {
        <p>目前還沒有田區。<a class="button button-outline" routerLink="/dashboard/fields/new">前往新增田區</a></p>
      }
    }
    @if (field(); as selected) {
      @let area = fieldArea();
      @if (area && area.id === selected.id) {
        <p>田區總面積 {{ area.total | number:'1.0-4' }} m²、
          已分配面積 {{ area.allocated | number:'1.0-4' }} m²、
          剩餘面積 {{ area.remaining | number:'1.0-4' }} m²</p>
      }
    } @else if (availableFields()?.length) {
      <p>選擇田區後可查看剩餘面積。</p>
    }

    @if (field() || availableFields()) {
      <form
        class="panel field-form"
        [formGroup]="form"
        (ngSubmit)="save(completeDialog)"
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
                @for (status of statuses(); track status) {
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
            @if (plantingId || status() !== "planned") {
              <div class="field-control">
                <label for="actual-date"
                  >實際栽種日（{{
                    requiresActualDate() ? "必填" : "選填"
                  }}）</label
                >
                <input
                  id="actual-date"
                  type="date"
                  formControlName="actualPlantingDate"
                  [required]="requiresActualDate()"
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
            <div class="field-control">
              <label for="planting-area">占用面積（m²，必填）</label>
              <input
                id="planting-area"
                inputmode="decimal"
                required
                formControlName="areaSquareMeters"
                [attr.aria-invalid]="errors().areaSquareMeters ? 'true' : null"
                aria-describedby="planting-area-error"
              />
              <p class="error" id="planting-area-error">
                {{ errors().areaSquareMeters?.[0] }}
              </p>
            </div>
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
            <button
              class="button button-outline"
              type="button"
              (click)="back()"
            >
              取消
            </button>
            <button class="button" type="submit" [disabled]="!field()">
              {{ saving() ? "儲存中…" : "儲存" }}
            </button>
          </div>
        </fieldset>
        @if (message()) {
          <p class="error field-form-message" role="alert">{{ message() }}</p>
        }
        @if (saved()) {
          <a routerLink="/dashboard/plantings" [queryParams]="returnQuery"
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
        <a routerLink="/dashboard/plantings" [queryParams]="returnQuery"
          >返回作物栽培管理</a
        >
      </section>
    } @else {
      <p class="panel empty-state" role="status">正在載入田區…</p>
    }
    <dialog #completeDialog class="field-delete-dialog" aria-labelledby="complete-title">
      <h2 id="complete-title">確認結束栽培</h2>
      <p>結束後，這筆紀錄會移至栽培紀錄，並釋放占用面積。</p>
      <div class="field-form-actions">
        <button type="button" class="button button-outline" autofocus (click)="completeDialog.close()">取消</button>
        <button type="button" class="button" (click)="completeDialog.close(); save(completeDialog, true)">確認結束</button>
      </div>
    </dialog>
  `,
})
export class PlantingFormPage {
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fields = inject(FieldsService);
  private readonly plantings = inject(PlantingsService);
  private readonly route = inject(ActivatedRoute).snapshot;
  protected readonly fixedFieldId = this.route.paramMap.get("fieldId");
  protected readonly plantingId = this.route.paramMap.get("plantingId");
  protected readonly returnQuery = {
    fieldId: this.route.queryParamMap.get("fieldId") ?? (this.plantingId ? null : this.fixedFieldId),
  };
  protected readonly field = signal<FieldDto | null>(null);
  protected readonly availableFields = signal<FieldDto[] | null>(null);
  private readonly selectedFieldId = computed(() => this.field()?.id);
  protected readonly fieldArea = toSignal(toObservable(this.selectedFieldId).pipe(
    switchMap((fieldId) => fieldId
      ? this.plantings.listFieldPlantings({ fieldId }).pipe(
          map((data) => ({
            id: fieldId,
            total: data.areaSquareMeters,
            allocated: data.allocatedAreaSquareMeters,
            remaining: data.remainingAreaSquareMeters,
          })),
          startWith(null),
          catchError(() => of(null)),
        )
      : of(null)),
  ));
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly errors = signal<PlantingErrors>({});
  protected readonly message = signal("");
  private readonly originalStatus = signal<PlantingStatus | null>(null);
  protected readonly statuses = computed<PlantingStatus[]>(() => {
    switch (this.originalStatus()) {
      case "planned":
        return ["planned", "growing"];
      case "growing":
        return ["growing", "harvesting", "completed"];
      case "harvesting":
        return ["harvesting", "completed"];
      default:
        return ["planned", "growing", "harvesting"];
    }
  });
  protected readonly statusLabel = plantingStatusLabel;
  protected readonly form = inject(FormBuilder).nonNullable.group({
    cropName: "",
    variety: "",
    status: "planned" as PlantingStatus,
    areaSquareMeters: "",
    plannedPlantingDate: "",
    actualPlantingDate: "",
    notes: "",
  });
  protected readonly status = toSignal(this.form.controls.status.valueChanges, {
    initialValue: "planned",
  });
  protected readonly requiresActualDate = computed(
    () => this.status() === "growing" || this.status() === "completed",
  );

  ngOnInit(): void {
    this.loadField();
  }

  protected loadField(): void {
    this.message.set("");
    const fieldId = this.fixedFieldId;
    if (!fieldId) {
      this.fields.getFields().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: ({ fields }) => {
          this.availableFields.set(fields);
          this.selectField(this.returnQuery.fieldId ?? "");
        },
        error: (error: unknown) => this.showFailure(error, "目前無法載入田區，請再試一次。"),
      });
      return;
    }
    forkJoin({
      field: this.fields.getField({ fieldId }),
      planting: this.plantingId
        ? this.plantings.getPlanting({ plantingId: this.plantingId })
        : of(null),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ field, planting }) => {
          if (planting && planting.fieldId !== field.id) {
            this.message.set("找不到這筆栽培紀錄。");
            return;
          }
          if (planting?.status === "completed") {
            this.message.set("已結束的栽培紀錄無法編輯。");
            return;
          }
          if (planting) {
            this.originalStatus.set(planting.status);
            this.form.setValue({
              cropName: planting.cropName,
              variety: planting.variety ?? "",
              status: planting.status,
              plannedPlantingDate: planting.plannedPlantingDate ?? "",
              actualPlantingDate: planting.actualPlantingDate ?? "",
              areaSquareMeters: String(planting.areaSquareMeters),
              notes: planting.notes ?? "",
            });
          }
          this.field.set(field);
        },
        error: (error: unknown) =>
          this.showFailure(error, "目前無法載入田區，請再試一次。"),
      });
  }

  protected selectField(fieldId: string): void {
    this.field.set(this.availableFields()?.find((field) => field.id === fieldId) ?? null);
    this.errors.set({});
    this.message.set("");
  }

  protected save(completeDialog: HTMLDialogElement, confirmed = false): void {
    const field = this.field();
    if (this.saving() || this.saved() || !field) return;
    this.message.set("");
    const values = this.form.getRawValue();
    const cropName = values.cropName.trim();
    const errors: PlantingErrors = {};
    if (!cropName) errors.cropName = ["請輸入作物名稱。"];
    const area = values.areaSquareMeters.trim();
    const areaSquareMeters = Number(area);
    if (!area) {
      errors.areaSquareMeters = ["請填寫占用面積。"];
    } else if (
      !/^(?:\d+(?:\.\d{1,4})?|\.\d{1,4})$/.test(area) ||
      areaSquareMeters <= 0 ||
      areaSquareMeters > 9999999999.9999
    ) {
      errors.areaSquareMeters = ["請輸入大於 0、最多四位小數的面積。"];
    }
    if (this.requiresActualDate() && !values.actualPlantingDate) {
      errors.actualPlantingDate = ["請填寫實際栽種日。"];
    }
    this.errors.set(errors);
    if (Object.keys(errors).length > 0) {
      this.message.set("請檢查標示的欄位。");
      return;
    }
    if (values.status === "completed" && !confirmed) {
      completeDialog.showModal();
      return;
    }

    this.saving.set(true);
    const request: EditPlantingRqDto = {
      cropName,
      variety: values.variety.trim() || null,
      status: values.status,
      plannedPlantingDate: values.plannedPlantingDate || null,
      actualPlantingDate:
        !this.plantingId && values.status === "planned"
          ? null
          : values.actualPlantingDate || null,
      areaSquareMeters,
      notes: values.notes.trim() || null,
    };
    const save = this.plantingId
      ? this.plantings.editPlanting({
          plantingId: this.plantingId,
          editPlantingRqDto: request,
        })
      : this.plantings.createPlanting({
          fieldId: field.id,
          createPlantingRqDto: {
            ...request,
            status: values.status as CreatePlantingRqDto.StatusEnum,
          },
        });
    save
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
            (error.status === 400 || error.status === 409) &&
            error.error?.errors
          ) {
            this.errors.set(error.error.errors);
            this.message.set("請檢查標示的欄位。");
          } else {
            this.showFailure(error, "目前無法儲存栽培紀錄，請再試一次。");
          }
        },
      });
  }

  protected back(saved = false): void {
    const fieldId = saved && !this.plantingId && this.returnQuery.fieldId
      ? this.field()?.id
      : this.returnQuery.fieldId;
    void this.router.navigate(["/dashboard/plantings"], {
      queryParams: { fieldId },
      info: saved
        ? { edited: !!this.plantingId }
        : undefined,
    });
  }

  private showFailure(error: unknown, message: string): void {
    if (error instanceof HttpErrorResponse && error.status === 401) {
      void this.router.navigateByUrl("/login", { replaceUrl: true });
    } else {
      this.message.set(
        error instanceof HttpErrorResponse && error.status === 404
          ? this.plantingId
            ? "找不到可存取的栽培紀錄。"
            : "找不到可存取的田區。"
          : message,
      );
    }
  }
}
